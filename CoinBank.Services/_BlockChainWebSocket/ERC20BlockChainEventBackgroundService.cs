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
        private const string LogPrefix = "[ERC20]";

        private readonly BlockChainSettings blockChainSettings;
        private readonly ITransactionLogService _transactionLogService;
        private readonly ISwapService _swapService;
        private readonly ILogger<ERC20BlockChainEventBackgroundService> _logger;
        private readonly AvailableTokensSettings _availableTokensSettings;

        private int _reconnectAttempts = 0;
        private DateTime _lastEventReceived = DateTime.UtcNow;

        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
        private readonly SemaphoreSlim _cleanupLock = new(1, 1);
        private readonly object _blockLock = new();

        private bool _isCleaningUp = false;
        private bool _useSecondaryWsUrl = false;
        private bool _isDisposed = false;

        private IDisposable _incomingTransferSubscription;
        private IDisposable _swapContractEventsSubscription;

        private BigInteger _swapLastProcessedBlock = 0;

        private Web3 _web3;
        private StreamingWebSocketClient _webSocketClient;

        public ERC20BlockChainEventBackgroundService(
            BlockChainSettings blockChainSettings,
            ITransactionLogService transactionLogService,
            ISwapService swapService,
            AvailableTokensSettings availableTokensSettings,
            ILogger<ERC20BlockChainEventBackgroundService> logger)
        {
            this.blockChainSettings = blockChainSettings;
            _transactionLogService = transactionLogService;
            _swapService = swapService;
            _availableTokensSettings = availableTokensSettings;
            _logger = logger;

            _web3 = new Web3(blockChainSettings.ERC20WsUrl);
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{Prefix} Service started", LogPrefix);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TryConnectWithRetryAsync(stoppingToken);
                    _lastEventReceived = DateTime.UtcNow;

                    while (_webSocketClient?.IsStarted == true && !stoppingToken.IsCancellationRequested)
                    {
                        if ((DateTime.UtcNow - _lastEventReceived).TotalMinutes > 3)
                        {
                            //_logger.LogWarning("{Prefix} No events received for 3 minutes. Reconnecting...", LogPrefix);
                            await TryConnectWithRetryAsync(stoppingToken);
                            _lastEventReceived = DateTime.UtcNow;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }

                    await Task.Delay(2000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    //_logger.LogInformation("{Prefix} Shutdown requested", LogPrefix);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Prefix} Unexpected error in service loop", LogPrefix);
                    await Task.Delay(5000, stoppingToken);
                }
            }

            _logger.LogInformation("{Prefix} Service stopped", LogPrefix);
        }


        private async Task TryConnectWithRetryAsync(CancellationToken stoppingToken)
        {
            if (!await _reconnectLock.WaitAsync(0, stoppingToken))
                return;

            try
            {
                _reconnectAttempts = 0;

                while (!stoppingToken.IsCancellationRequested &&
                       _reconnectAttempts < blockChainSettings.MaxReconnectAttempts)
                {
                    try
                    {
                        //_logger.LogInformation("{Prefix} Connecting WebSocket (Attempt {Attempt}/{Max})",
                            //LogPrefix, _reconnectAttempts + 1, blockChainSettings.MaxReconnectAttempts);

                        await ConnectAndSubscribe(stoppingToken);

                        _reconnectAttempts = 0;
                        return;
                    }
                    catch (Exception ex)
                    {
                        _reconnectAttempts++;

                        _logger.LogWarning(ex,
                            //"{Prefix} Connection failed, retrying...",
                            LogPrefix);

                        await Task.Delay(CalculateReconnectDelay(), stoppingToken);
                    }
                }

                _logger.LogCritical("{Prefix} Max reconnect attempts reached, cooling down...",
                    LogPrefix);

                await Task.Delay(30000, stoppingToken);
                _reconnectAttempts = 0;
            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        private TimeSpan CalculateReconnectDelay()
        {
            var delaySeconds = Math.Min(Math.Pow(2, _reconnectAttempts) * blockChainSettings.ReconnectInterval, 300);
            return TimeSpan.FromSeconds(delaySeconds);
        }

        private async Task ConnectAndSubscribe(CancellationToken cancellationToken)
        {
            //_logger.LogInformation("{Prefix} Connecting WebSocket...", LogPrefix);

            await CleanupConnection();

            var url = GetCurrentWsUrl();

            _webSocketClient = new StreamingWebSocketClient(url);
            _web3 = new Web3(url);

            await _webSocketClient.StartAsync();

            await SubscribeToSwapContractEventsAsync(cancellationToken);
            await SubscribeToIncomingTransfersAsync(cancellationToken);

            _logger.LogInformation("{Prefix} Subscriptions active", LogPrefix);
        }

        private string GetCurrentWsUrl()
        {
            var url = blockChainSettings.ERC20WsUrl;
            //_logger.LogInformation("{Prefix} Using WS: {Url}", LogPrefix, url);
            return url;
        }

        private async Task CleanupConnection()
        {
            if (!await _cleanupLock.WaitAsync(0))
            {
                _logger.LogInformation("{Prefix} Cleanup already running", LogPrefix);
                return;
            }

            try
            {
                //_logger.LogInformation("{Prefix} Cleaning up connection...", LogPrefix);

                _incomingTransferSubscription?.Dispose();
                _swapContractEventsSubscription?.Dispose();

                _incomingTransferSubscription = null;
                _swapContractEventsSubscription = null;

                if (_webSocketClient != null)
                {
                    try
                    {
                        if (_webSocketClient.IsStarted)
                            await _webSocketClient.StopAsync();
                    }
                    catch { }

                    _webSocketClient.Dispose();
                    _webSocketClient = null;
                }

                _logger.LogInformation("{Prefix} Cleanup completed", LogPrefix);
            }
            finally
            {
                _cleanupLock.Release();
            }
        }

        private async Task SubscribeToSwapContractEventsAsync(CancellationToken cancellationToken)
        {
            var subscription = new EthLogsObservableSubscription(_webSocketClient);

            var contracts = new[] { blockChainSettings.ERC20SwapContractAddress };

            var observable = subscription.GetSubscriptionDataResponsesAsObservable()
                .Where(log => contracts.Any(c => log.Address.IsTheSameAddress(c)))
                .Select(log => Observable.FromAsync(() => SwapProcessContractEventLogAsync(log, cancellationToken)))
                .Concat();

            _swapContractEventsSubscription = observable.Subscribe(
                _ => { },
                ex => _logger.LogError(ex, "{Prefix} Swap subscription error", LogPrefix),
                () => _logger.LogWarning("{Prefix} Swap subscription closed", LogPrefix)
            );

            await subscription.SubscribeAsync(new NewFilterInput
            {
                Address = contracts,
                FromBlock = new BlockParameter(await GetSwapLastProcessedBlock(cancellationToken))
            });
        }

        private async Task SwapProcessContractEventLogAsync(FilterLog log, CancellationToken cancellationToken)
        {
            try
            {
                _lastEventReceived = DateTime.UtcNow;

                var network = "ERC20";

                var initiated = log.DecodeEvent<SwapInitiatedEventDTO>();
                if (initiated != null)
                {
                    await HandleSwapInitiated(log, initiated, network);
                    return;
                }

                var executed = log.DecodeEvent<SwapExecutedEventDTO>();
                if (executed != null)
                {
                    await HandleSwapExecuted(log, executed, network);
                    return;
                }

                var failed = log.DecodeEvent<SwapFailedEventDTO>();
                if (failed != null)
                {
                    await HandleSwapFailed(log, failed, network);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} Swap decode error", LogPrefix);
            }
        }


        private async Task SubscribeToIncomingTransfersAsync(CancellationToken cancellationToken)
        {
            var tokens = _availableTokensSettings.Select(t => t.Address.ToLower()).ToList();

            var subscription = new EthLogsObservableSubscription(_webSocketClient);

            var observable = subscription.GetSubscriptionDataResponsesAsObservable()
                .Where(log => tokens.Contains(log.Address.ToLower()))
                .Select(log => Observable.FromAsync(() => ProcessIncomingTransferLogAsync(log)))
                .Concat();

            _incomingTransferSubscription = observable.Subscribe(
                _ => { },
                ex => _logger.LogError(ex, "{Prefix} Transfer subscription error", LogPrefix),
                () => _logger.LogWarning("{Prefix} Transfer subscription closed", LogPrefix)
            );

            await subscription.SubscribeAsync(new NewFilterInput
            {
                Address = tokens.ToArray()
            });
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
                    _logger.LogWarning("{Prefix} Token not found: {Address}", LogPrefix, log.Address);
                    return;
                }

                var amount = Web3.Convert.FromWei(transfer.Event.Value);

                if (transfer.Event.To.IsTheSameAddress(blockChainSettings.ERC20SwapContractAddress))
                {
                    _logger.LogInformation(
                        "{Prefix} Swap deposit: {Token} {Amount} from {From}",
                        LogPrefix, token.Name, amount, transfer.Event.From);

                    await _swapService.UpdateSingleTokenInStorageAsync(token.Address,token.Network);

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} Transfer processing error", LogPrefix);
            }
        }


        private static string ByteArray32ToHex(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length != 32) throw new ArgumentException("Must be 32 bytes");

            return "0x" + bytes.ToHex();
        }


        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_isDisposed) return;

            _logger.LogInformation("{Prefix} Stopping service...", LogPrefix);

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


        private async Task HandleSwapInitiated(FilterLog log, EventLog<SwapInitiatedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);

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

        private async Task<HexBigInteger> GetSwapLastProcessedBlock(CancellationToken cancellationToken)
        {
            lock (_blockLock)
            {
                if (_swapLastProcessedBlock > 0)
                    return _swapLastProcessedBlock.ToHexBigInteger();
            }

            var dbBlock = await _transactionLogService.GetSwapLastCheckedBlockNumberAsync("ERC20");

            lock (_blockLock)
            {
                _swapLastProcessedBlock = dbBlock;
            }

            if (_swapLastProcessedBlock > 0)
                return _swapLastProcessedBlock.ToHexBigInteger();

            var _web3Client = new Web3(blockChainSettings.ERC20RpcUrl);
            var latestBlockNumber = await _web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();

            lock (_blockLock)
            {
                _swapLastProcessedBlock = latestBlockNumber;
                return latestBlockNumber;
            }
        }
   
    
    }
}
