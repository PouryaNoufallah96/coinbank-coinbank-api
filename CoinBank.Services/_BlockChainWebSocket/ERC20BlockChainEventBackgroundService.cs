using CoinBank.Domain.Collections;
using CoinBank.Services._BlockChain.DTOs.Settings;
using CoinBank.Services._BlockChainWebSocket.DTOs;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._Swap;
using CoinBank.Services._Transaction;
using CoinBank.Services._Transaction.DTOs.Updates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.Reactive.Eth.Subscriptions;
using Nethereum.Util;
using Nethereum.Web3;
using System.Numerics;
using System.Reactive.Linq;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._BlockChainWebSocket
{
    public class ERC20BlockChainEventBackgroundService : BackgroundService, IHostedDependency
    {
        private const string SwapLogPrefix = "[ERC20-WS-Swap]";
        private const string TransferLogPrefix = "[ERC20-WS-Transfer]";
        private const string CommonLogPrefix = "[ERC20-WS]";
        private const string NetworkName = "ERC20";

        private readonly BlockChainSettings _blockChainSettings;
        private readonly ITransactionLogService _transactionLogService;
        private readonly ISwapService _swapService;
        private readonly AvailableTokensSettings _availableTokensSettings;
        private readonly ILogger<ERC20BlockChainEventBackgroundService> _logger;

        private Web3 _web3;
        private StreamingWebSocketClient _webSocketClient;

        private readonly string[] _rpcUrls;
        private readonly string[] _wsUrls;

        private int _currentRpcIndex = 0;
        private int _currentWsIndex = 0;

        private readonly string _swapContractAddress;

        private readonly object _blockLock = new();

        private BigInteger _swapLastProcessedBlock = 0;

        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
        private readonly SemaphoreSlim _cleanupLock = new(1, 1);

        private IDisposable _incomingTransferSubscription;
        private IDisposable _swapContractEventsSubscription;

        private int _reconnectAttempts = 0;
        private DateTime _lastEventReceived = DateTime.UtcNow;

        private bool _isDisposed = false;

        public ERC20BlockChainEventBackgroundService(
            BlockChainSettings blockChainSettings,
            ITransactionLogService transactionLogService,
            ISwapService swapService,
            AvailableTokensSettings availableTokensSettings,
            ILogger<ERC20BlockChainEventBackgroundService> logger)
        {
            _blockChainSettings = blockChainSettings;
            _transactionLogService = transactionLogService;
            _swapService = swapService;
            _availableTokensSettings = availableTokensSettings;
            _logger = logger;

            _rpcUrls = new[] { _blockChainSettings.ERC20RpcUrl };
            _wsUrls = new[] { _blockChainSettings.ERC20WsUrl };

            _swapContractAddress = _blockChainSettings.ERC20SwapContractAddress;

            InitializeClients();
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{Prefix} Service started", CommonLogPrefix);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TryConnectWithRetryAsync(stoppingToken);

                    _lastEventReceived = DateTime.UtcNow;

                    while (_webSocketClient?.IsStarted == true && !stoppingToken.IsCancellationRequested)
                    {
                        var now = DateTime.UtcNow;

                        if ((now - _lastEventReceived).TotalMinutes > 2)
                        {
                            _logger.LogWarning("{Prefix} Heartbeat timeout detected. Reconnecting...", CommonLogPrefix);
                            await Task.Delay(2000, stoppingToken);
                            await TryConnectWithRetryAsync(stoppingToken);
                            _lastEventReceived = DateTime.UtcNow;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }

                    if (_webSocketClient != null && !_webSocketClient.IsStarted)
                    {
                        await Task.Delay(2000, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Prefix} Unexpected error in service loop", CommonLogPrefix);

                    SwitchRpc();
                    SwitchWs();
                    InitializeClients();

                    await Task.Delay(5000, stoppingToken);
                }
            }

            _logger.LogInformation("{Prefix} Service stopped", CommonLogPrefix);
        }


        private async Task TryConnectWithRetryAsync(CancellationToken stoppingToken)
        {
            if (!await _reconnectLock.WaitAsync(0, stoppingToken))
            {
                return;
            }

            try
            {
                _reconnectAttempts = 0;

                while (!stoppingToken.IsCancellationRequested && _reconnectAttempts < Math.Max(1, _blockChainSettings.MaxReconnectAttempts))
                {
                    try
                    {
                        await ConnectAndSubscribe(stoppingToken);
                        _reconnectAttempts = 0;
                        return;
                    }
                    catch (Exception ex)
                    {
                        _reconnectAttempts++;

                        _logger.LogWarning(ex, "{Prefix} Connection failed. Retry: {Retry}", CommonLogPrefix, _reconnectAttempts);

                        SwitchRpc();
                        SwitchWs();
                        InitializeClients();

                        await Task.Delay(CalculateReconnectDelay(), stoppingToken);
                    }
                }

                if (_reconnectAttempts >= Math.Max(1, _blockChainSettings.MaxReconnectAttempts))
                {
                    await Task.Delay(30000, stoppingToken);
                    _reconnectAttempts = 0;
                }
            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        private TimeSpan CalculateReconnectDelay()
        {
            var interval = Math.Max(1, _blockChainSettings.ReconnectInterval);
            var delaySeconds = Math.Min(Math.Pow(2, _reconnectAttempts) * interval, 300);
            return TimeSpan.FromSeconds(delaySeconds);
        }

        private async Task ConnectAndSubscribe(CancellationToken cancellationToken)
        {
            await CleanupConnection();

            var wsUrl = GetCurrentWsUrl();
            _webSocketClient = new StreamingWebSocketClient(wsUrl);
            _web3 = new Web3(GetCurrentRpcUrl());

            try
            {
                await _webSocketClient.StartAsync();

                await SubscribeToSwapContractEventsAsync(cancellationToken);
                await SubscribeToIncomingTransfersAsync(cancellationToken);

                _logger.LogInformation("{Prefix} Subscriptions active", SwapLogPrefix);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} ConnectAndSubscribe failed", SwapLogPrefix);
                throw;
            }
        }

        private void InitializeClients()
        {
            _web3 = new Web3(GetCurrentRpcUrl());
        }
        
        private string GetCurrentRpcUrl()
        {
            return _rpcUrls[_currentRpcIndex];
        }

        private string GetCurrentWsUrl()
        {
            return _wsUrls[_currentWsIndex];
        }

        private void SwitchRpc()
        {
            _currentRpcIndex = (_currentRpcIndex + 1) % _rpcUrls.Length;
        }

        private void SwitchWs()
        {
            _currentWsIndex = (_currentWsIndex + 1) % _wsUrls.Length;
        }

        private async Task CleanupConnection()
        {
            if (!await _cleanupLock.WaitAsync(0))
            {
                _logger.LogInformation("{Prefix} Cleanup already running", CommonLogPrefix);
                return;
            }

            try
            {
                _incomingTransferSubscription?.Dispose();
                _incomingTransferSubscription = null;

                _swapContractEventsSubscription?.Dispose();
                _swapContractEventsSubscription = null;

                if (_webSocketClient != null)
                {
                    try
                    {
                        if (_webSocketClient.IsStarted)
                        {
                            await _webSocketClient.StopAsync();
                        }
                    }
                    catch { }

                    try
                    {
                        _webSocketClient.Dispose();
                    }
                    catch { }

                    _webSocketClient = null;
                }

                _logger.LogInformation("{Prefix} Cleanup completed", CommonLogPrefix);
            }
            finally
            {
                _cleanupLock.Release();
            }
        }



        #region Swap 

        private async Task SubscribeToSwapContractEventsAsync(CancellationToken cancellationToken)
        {
            var subscription = new EthLogsObservableSubscription(_webSocketClient);

            var contracts = new[] { _swapContractAddress };

            var observable = subscription.GetSubscriptionDataResponsesAsObservable()
                .Where(log => contracts.Any(c => log.Address.IsTheSameAddress(c)))
                .Select(log => Observable.FromAsync(() => SwapProcessContractEventLogAsync(log, cancellationToken)))
                .Concat();

            _swapContractEventsSubscription = observable.Subscribe(
                _ => { },
                ex => _logger.LogError(ex, "{Prefix} Swap subscription error", SwapLogPrefix),
                () => _logger.LogWarning("{Prefix} Swap subscription closed", SwapLogPrefix)
            );

            var filter = new NewFilterInput
            {
                Address = contracts,
                FromBlock = new BlockParameter(await GetSwapLastProcessedBlock(cancellationToken))
            };

            await subscription.SubscribeAsync(filter);
        }

        private async Task SwapProcessContractEventLogAsync(FilterLog log, CancellationToken cancellationToken)
        {
            try
            {
                _lastEventReceived = DateTime.UtcNow;
                var network = NetworkName;

                var initiated = log.DecodeEvent<SwapInitiatedEventDTO>();
                if (initiated != null)
                {
                    await HandleSwapInitiated(log, initiated, network);
                    UpdateSwapLastBlock(log);
                    return;
                }

                var executed = log.DecodeEvent<SwapExecutedEventDTO>();
                if (executed != null)
                {
                    await HandleSwapExecuted(log, executed, network);
                    //UpdateSwapLastBlock(log);
                    return;
                }

                var failed = log.DecodeEvent<SwapFailedEventDTO>();
                if (failed != null)
                {
                    await HandleSwapFailed(log, failed, network);
                    //UpdateSwapLastBlock(log);
                    return;
                }

                var completed = log.DecodeEvent<SwapCompletedEventDTO>();
                if (completed != null)
                {
                    await HandleSwapCompleted(log, completed, network);
                    //UpdateSwapLastBlock(log);
                    return;
                }

                var refunded = log.DecodeEvent<SwapRefundedEventDTO>();
                if (refunded != null)
                {
                    await HandleSwapRefunded(log, refunded, network);
                    //UpdateSwapLastBlock(log);
                    return;
                }

                var refundClaimed = log.DecodeEvent<TokenRefundClaimedEventDTO>();
                if (refundClaimed != null)
                {
                    await HandleTokenRefundClaimed(log, refundClaimed, NetworkName);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} Swap decode error", SwapLogPrefix);
            }
        }

        private async Task HandleSwapInitiated(FilterLog log, EventLog<SwapInitiatedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);

            _logger.LogInformation("{Prefix} SwapInit | SwapId: {SwapId} | TokenOut: {TokenOut} | AmountOut: {AmountOut} | Receiver: {Receiver}",
              SwapLogPrefix, swapId, ev.Event.TokenOut, ev.Event.AmountOut, ev.Event.Receiver);

            await _transactionLogService.CreateSwapInitiatedLogAsync(new SwapInitiatedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                SwapId = swapId,
                Buyer = ev.Event.Receiver,
                SourceTokenAddress = ev.Event.TokenIn,
                DestinationTokenAddress = ev.Event.TokenOut,
                DesEid = ev.Event.DstEid,
                SourceTokenAmount = ev.Event.AmountIn,
                DestinationTokenAmount = ev.Event.AmountOut,
                DestinationWallet = ev.Event.Receiver,
                Fee = ev.Event.Fee.ToString(),
                Network = network,
                EventType = BlockchainEventType.SwapInitiated
            });
        }

        private async Task HandleSwapExecuted(FilterLog log, EventLog<SwapExecutedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);
            _logger.LogInformation("{Prefix} SwapExecuted | SwapId: {SwapId} | TokenOut: {TokenOut} | AmountOut: {AmountOut} | Receiver: {Receiver}",
               SwapLogPrefix, swapId, ev.Event.TokenOut, ev.Event.AmountOut, ev.Event.Receiver);

            await _transactionLogService.CreateSwapExecutedLogAsync(new SwapExecutedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                SwapId = swapId,
                DestinationTokenAddress = ev.Event.TokenOut,
                DestinationTokenAmount = ev.Event.AmountOut,
                DestinationWallet = ev.Event.Receiver,
                Network = network,
                EventType = BlockchainEventType.SwapExecuted
            });
        }

        private async Task HandleSwapFailed(FilterLog log, EventLog<SwapFailedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);
            _logger.LogInformation("{Prefix} SwapFailed | SwapId: {SwapId} | TokenOut: {TokenOut} | AmountOut: {AmountOut} | Receiver: {Receiver}",
              SwapLogPrefix, swapId, ev.Event.TokenOut, ev.Event.AmountOut, ev.Event.Receiver);
            await _transactionLogService.CreateSwapFailedLogAsync(new SwapFailedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                SwapId = swapId,
                DestinationTokenAddress = ev.Event.TokenOut,
                DestinationTokenAmount = ev.Event.AmountOut,
                DestinationWallet = ev.Event.Receiver,
                Network = network,
                EventType = BlockchainEventType.SwapFailed
            });
        }

        private async Task HandleSwapCompleted(FilterLog log, EventLog<SwapCompletedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);
            _logger.LogInformation("{Prefix} SwapCompleted | SwapId: {SwapId}", SwapLogPrefix, swapId);
            await _transactionLogService.CreateSwapCompletedLogAsync(new SwapCompletedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                SwapId = swapId,
                Network = network,
                EventType = BlockchainEventType.SwapCompleted
            });
        }

        private async Task HandleSwapRefunded(FilterLog log, EventLog<SwapRefundedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);
            _logger.LogInformation("{Prefix} SwapRefunded | SwapId: {SwapId} | Token: {Token} | Amount: {Amount} | User: {User}",
               SwapLogPrefix, swapId, ev.Event.Token, ev.Event.Amount, ev.Event.User);
            await _transactionLogService.CreateSwapRefundedLogAsync(new SwapRefundedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                SwapId = swapId,
                Token = ev.Event.Token,
                Amount = ev.Event.Amount,
                User = ev.Event.User,
                Network = network,
                EventType = BlockchainEventType.SwapRefunded
            });
        }

        private async Task HandleTokenRefundClaimed(FilterLog log, EventLog<TokenRefundClaimedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);

            _logger.LogInformation(
                "{Prefix} TokenRefundClaimed | SwapId: {SwapId} | Token: {Token} | Recipient: {Recipient} | Amount: {Amount}",
                SwapLogPrefix,
                swapId,
                ev.Event.Token,
                ev.Event.Recipient,
                ev.Event.Amount);

            await _transactionLogService.CreateSwapRefundClaimedLogAsync(
                new SwapRefundClaimedLog
                {
                    Hash = log.TransactionHash,
                    Address = log.Address,
                    BlockNumber = log.BlockNumber.Value,
                    SwapId = swapId,
                    Token = ev.Event.Token,
                    Recipient = ev.Event.Recipient,
                    Amount = ev.Event.Amount,
                    User = ev.Event.Recipient,
                    Network = network,
                    EventType = BlockchainEventType.SwapRefundClaimed
                });
        }

        private async Task<HexBigInteger> GetSwapLastProcessedBlock(CancellationToken cancellationToken)
        {
            lock (_blockLock)
            {
                if (_swapLastProcessedBlock > 0)
                    return _swapLastProcessedBlock.ToHexBigInteger();
            }

            var dbBlock = await _transactionLogService.GetSwapLastCheckedBlockNumberAsync(NetworkName);

            lock (_blockLock)
            {
                _swapLastProcessedBlock = dbBlock;
            }

            if (_swapLastProcessedBlock > 0)
                return _swapLastProcessedBlock.ToHexBigInteger();

            var latestBlockNumber = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

            lock (_blockLock)
            {
                _swapLastProcessedBlock = latestBlockNumber;
                return latestBlockNumber;
            }
        }

        private void UpdateSwapLastBlock(FilterLog log)
        {
            if (log?.BlockNumber == null) return;

            lock (_blockLock)
            {
                _swapLastProcessedBlock = BigInteger.Max(_swapLastProcessedBlock, log.BlockNumber.Value + 1);
            }
        }

        #endregion


        #region Incoming Transfers
     
        private async Task SubscribeToIncomingTransfersAsync(CancellationToken cancellationToken)
        {
            var tokens = _availableTokensSettings.Select(t => t.Address.ToLower()).ToArray();

            var subscription = new EthLogsObservableSubscription(_webSocketClient);

            var observable = subscription.GetSubscriptionDataResponsesAsObservable()
                .Where(log => tokens.Contains(log.Address.ToLower()))
                .Select(log => Observable.FromAsync(() => ProcessIncomingTransferLogAsync(log)))
                .Concat();

            _incomingTransferSubscription = observable.Subscribe(
                _ => { },
                ex => _logger.LogError(ex, "{Prefix} Transfer subscription error", TransferLogPrefix),
                () => _logger.LogWarning("{Prefix} Transfer subscription closed", TransferLogPrefix)
            );

            var filter = new NewFilterInput
            {
                Address = tokens
            };

            await subscription.SubscribeAsync(filter);
        }

        private async Task ProcessIncomingTransferLogAsync(FilterLog log)
        {
            try
            {
                _lastEventReceived = DateTime.UtcNow;

                var transfer = log.DecodeEvent<TransferEventDTO>();
                if (transfer == null) return;

                var token = _availableTokensSettings.FirstOrDefault(t =>
                    t.Address.IsTheSameAddress(log.Address));

                if (token == null)
                {
                    _logger.LogWarning("{Prefix} Token not found: {Address}", TransferLogPrefix, log.Address);
                    return;
                }

                var amount = Web3.Convert.FromWei(transfer.Event.Value);

                if (transfer.Event.To.IsTheSameAddress(_blockChainSettings.ERC20SwapContractAddress))
                {
                    _logger.LogInformation(
                        "{Prefix} Swap deposit: {Token} {Amount} from {From}",
                        SwapLogPrefix, token.Name, amount, transfer.Event.From);

                    await _swapService.UpdateSingleTokenInStorageAsync(token.Address, token.Network);

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} Transfer processing error", TransferLogPrefix);
            }
        }

        #endregion


        private static string ByteArray32ToHex(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length != 32) throw new ArgumentException("Must be 32 bytes");

            return "0x" + bytes.ToHex();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_isDisposed) return;

            _logger.LogInformation("{Prefix} Stopping service...", SwapLogPrefix);

            await CleanupConnection();

            _isDisposed = true;
            await base.StopAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _incomingTransferSubscription?.Dispose();
            _swapContractEventsSubscription?.Dispose();
            _webSocketClient?.Dispose();

            _reconnectLock.Dispose();
            _cleanupLock.Dispose();

            _isDisposed = true;
        }

    }
}
