using CoinBank.Domain.Collections;
using CoinBank.Services._BlockChain.DTOs.Settings;
using CoinBank.Services._BlockChainWebSocket.DTOs;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._PreSale;
using CoinBank.Services._Swap;
using CoinBank.Services._Transaction;
using CoinBank.Services._Transaction.DTOs.Updates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.BlockchainProcessing.BlockStorage.Entities;
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
    public class BEP20BlockChainEventBackgroundService : BackgroundService, IHostedDependency
    {
        private const string SwapLogPrefix = "[BEP20-WS-Swap]";
        private const string TransferLogPrefix = "[BEP20-WS-Transfer]";
        private const string StakeLogPrefix = "[BEP20-STAKE-WS]";
        private const string PreSaleLogPrefix = "[BEP20-WS-PreSale]";
        private const string CommonLogPrefix = "[BEP20-WS]";
        private const string NetworkName = "BEP20";

        private readonly BlockChainSettings _blockChainSettings;
        private readonly ITransactionLogService _transactionLogService;
        private readonly IPreSaleService _preSaleService;
        private readonly ILogger<BEP20BlockChainEventBackgroundService> _logger;
        private readonly AvailableTokensSettings _availableTokensSettings;
        private readonly ISwapService _swapService;

        private Web3 _web3;
        private StreamingWebSocketClient _webSocketClient;

        private readonly string[] _rpcUrls;
        private readonly string[] _wsUrls;

        private int _currentRpcIndex = 0;
        private int _currentWsIndex = 0;

        private readonly object _blockLock = new();

        private BigInteger _swapLastProcessedBlock = 0;
        private BigInteger _preSaleLastProcessedBlock = 0;
        private BigInteger _stakeLastProcessedBlock = 0;


        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
        private readonly SemaphoreSlim _cleanupLock = new(1, 1);

        private IDisposable _preSaleContractEventsSubscription;
        private IDisposable _incomingTransferSubscription;
        private IDisposable _swapContractEventsSubscription;
        private IDisposable _stakeContractEventsSubscription;


        private int _reconnectAttempts = 0;
        private DateTime _lastEventReceived = DateTime.UtcNow;

        private bool _isDisposed = false;

        public BEP20BlockChainEventBackgroundService(
            BlockChainSettings blockChainSettings,
            ITransactionLogService transactionLogService,
            IPreSaleService preSaleService,
            AvailableTokensSettings availableTokensSettings,
            ISwapService swapService,
            ILogger<BEP20BlockChainEventBackgroundService> logger)
        {
            _blockChainSettings = blockChainSettings;
            _transactionLogService = transactionLogService;
            _preSaleService = preSaleService;
            _availableTokensSettings = availableTokensSettings;
            _swapService = swapService;
            _logger = logger;

            _rpcUrls = new[] { _blockChainSettings.BEP20RpcUrl, _blockChainSettings.BEP20RpcUrl2 };
            _wsUrls = new[] { _blockChainSettings.BEP20WsUrl, _blockChainSettings.BEP20WsUrl2 };

            InitializeClients();
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TryConnectWithRetryAsync(stoppingToken);
                    _lastEventReceived = DateTime.UtcNow;

                    //_logger.LogInformation("{Prefix} Connected to BEP20 blockchain", LogPrefix);

                    while (_webSocketClient?.IsStarted == true && !stoppingToken.IsCancellationRequested)
                    {
                        if ((DateTime.UtcNow - _lastEventReceived).TotalMinutes > 2)
                        {
                            //_logger.LogWarning("{Prefix} No BEP20 events received. Reconnecting...", LogPrefix);

                            await Task.Delay(2000, stoppingToken);
                            await TryConnectWithRetryAsync(stoppingToken);

                            _lastEventReceived = DateTime.UtcNow;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }

                    await Task.Delay(2000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("{Prefix} Shutdown requested", CommonLogPrefix);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Prefix} Unexpected error", CommonLogPrefix);
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

                await SubscribeToPreSaleContractEventsAsync(cancellationToken);
                await SubscribeToSwapContractEventsAsync(cancellationToken);
                await SubscribeToIncomingTransfersAsync(cancellationToken);

                _logger.LogInformation("{Prefix} Subscriptions active", CommonLogPrefix);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} ConnectAndSubscribe failed", CommonLogPrefix);
                throw;
            }
        }

        private async Task CleanupConnection()
        {
            if (!await _cleanupLock.WaitAsync(0))
            {
                _logger.LogInformation("Cleanup already in progress, skipping...");
                return;
            }

            try
            {
                _logger.LogInformation("Starting cleanup...");

                _preSaleContractEventsSubscription?.Dispose();
                _incomingTransferSubscription?.Dispose();
                _swapContractEventsSubscription?.Dispose();
                _stakeContractEventsSubscription?.Dispose();
                _preSaleContractEventsSubscription = null;
                _incomingTransferSubscription = null;
                _swapContractEventsSubscription = null;
                _stakeContractEventsSubscription = null;

                if (_webSocketClient != null)
                {
                    try
                    {
                        if (_webSocketClient.IsStarted)
                        {
                            await _webSocketClient.StopAsync();
                            await Task.Delay(300);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "WebSocket StopAsync failed or already stopped");
                    }

                    try
                    {
                        _webSocketClient.Dispose();
                    }
                    catch (SemaphoreFullException ex)
                    {
                        _logger.LogWarning(ex, "Ignoring SemaphoreFullException from WebSocket.Dispose()");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "WebSocket Dispose failed");
                    }

                    _webSocketClient = null;
                }

                _logger.LogInformation("Cleanup completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup.");
            }
            finally
            {
                _cleanupLock.Release();
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



        #region PreSaleSide
        private async Task SubscribeToPreSaleContractEventsAsync(CancellationToken cancellationToken)
        {
            var subscription = new EthLogsObservableSubscription(_webSocketClient);

            var safeObservable = subscription.GetSubscriptionDataResponsesAsObservable()
           .Where(log => log.Address.IsTheSameAddress(_blockChainSettings.PreSaleContractAddress))
           .Select(log => Observable.FromAsync(() => PreSaleProcessContractEventLogAsync(log, cancellationToken)))
           .Concat();

            _preSaleContractEventsSubscription = safeObservable.Subscribe(
                _ => { },
                async ex =>
                {
                    _logger.LogError(ex, "{Prefix} PreSale subscription error", PreSaleLogPrefix);
                },
                () =>
                {
                    _logger.LogWarning("{Prefix} PreSale subscription completed", PreSaleLogPrefix);
                });

            var filter = new NewFilterInput
            {
                Address = new[] { _blockChainSettings.PreSaleContractAddress },
                FromBlock = new BlockParameter(await GetPreSaleOrderLastProcessedBlock(cancellationToken))
            };

            await subscription.SubscribeAsync(filter);
        }

        private async Task PreSaleProcessContractEventLogAsync(FilterLog log, CancellationToken cancellationToken)
        {
            try
            {
                _lastEventReceived = DateTime.UtcNow;

                var preSaleOrderRegisteredEvent = log.DecodeEvent<PurchasedEventDTO>();
                if (preSaleOrderRegisteredEvent != null)
                {
                    await CreatePreSaleOrderCreateLogAsync(log, preSaleOrderRegisteredEvent);
                    return;
                }


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error decoding blockchain event");
            }
        }

        private async Task CreatePreSaleOrderCreateLogAsync(FilterLog log, EventLog<PurchasedEventDTO> purchasedEvent)
        {
            var saleId = ByteArray32ToHex(purchasedEvent.Event.SaleId);
            var orderId = ByteArray32ToHex(purchasedEvent.Event.OrderId);

            _logger.LogInformation(
                "{Prefix} Purchased: SaleId: {SaleId}, OrderId: {OrderId}, Buyer: {Buyer}, Purchased: {Purchased}, Paid: {Paid}",
                PreSaleLogPrefix,
                saleId,
                orderId,
                purchasedEvent.Event.Buyer,
                purchasedEvent.Event.AmountPurchased,
                purchasedEvent.Event.AmountPaid
            );

            SentrySdk.CaptureMessage(
                $"{PreSaleLogPrefix} Purchased: SaleId: {saleId}, OrderId: {orderId}, Buyer: {purchasedEvent.Event.Buyer}, Purchased: {purchasedEvent.Event.AmountPurchased}, Paid: {purchasedEvent.Event.AmountPaid}"
            );

            await _transactionLogService.CreatePreSaleOrderCreateLogAsync(new PreSaleOrderCreateLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                Buyer = purchasedEvent.Event.Buyer,
                SaleId = saleId,
                OrderId = orderId,
                AmountPurchased = purchasedEvent.Event.AmountPurchased.ToString(),
                AmountPaid = purchasedEvent.Event.AmountPaid.ToString(),
                EventType = BlockchainEventType.PreSaleOrderCreate
            });

            lock (_blockLock)
            {
                _preSaleLastProcessedBlock = BigInteger.Max(_preSaleLastProcessedBlock, log.BlockNumber.Value + 1);
            }
        }

        private async Task<HexBigInteger> GetPreSaleOrderLastProcessedBlock(CancellationToken cancellationToken)
        {
            try
            {
                lock (_blockLock)
                {
                    if (_preSaleLastProcessedBlock > 0)
                        return _preSaleLastProcessedBlock.ToHexBigInteger();
                }

                var lastDbBlock = await _transactionLogService.GetPreSaleOrderLastCheckedBlockNumberAsync();

                lock (_blockLock)
                {
                    _preSaleLastProcessedBlock = lastDbBlock;
                }

                if (_preSaleLastProcessedBlock > 0)
                    return _preSaleLastProcessedBlock.ToHexBigInteger();

                try
                {
                    var _web3Client = new Web3(_blockChainSettings.BEP20RpcUrl);
                    var latestBlockNumber = await _web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();

                    lock (_blockLock)
                    {
                        _preSaleLastProcessedBlock = latestBlockNumber;
                        return latestBlockNumber;
                    }

                }
                catch (Exception e)
                {
                    _logger.LogError(e.Message);
                    throw;
                }

            }
            catch (Exception e)
            {
                SentrySdk.CaptureException(e);
                throw;
            }
        }

        #endregion



        #region Swap

        private async Task SubscribeToSwapContractEventsAsync(CancellationToken cancellationToken)
        {
            var subscription = new EthLogsObservableSubscription(_webSocketClient);

            var contracts = new[]
            {
                _blockChainSettings.BEP20SwapContractAddress
            };

            var observable = subscription.GetSubscriptionDataResponsesAsObservable()
                .Where(log => log.Address.IsTheSameAddress(_blockChainSettings.BEP20SwapContractAddress))
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

                var initiated = log.DecodeEvent<SwapInitiatedEventDTO>();
                if (initiated != null)
                {
                    await HandleSwapInitiated(log, initiated, NetworkName);
                    UpdateSwapLastBlock(log);
                    return;
                }

                var executed = log.DecodeEvent<SwapExecutedEventDTO>();
                if (executed != null)
                {
                    await HandleSwapExecuted(log, executed, NetworkName);
                    //UpdateSwapLastBlock(log);
                    return;
                }

                var failed = log.DecodeEvent<SwapFailedEventDTO>();
                if (failed != null)
                {
                    await HandleSwapFailed(log, failed, NetworkName);
                    //UpdateSwapLastBlock(log);
                    return;
                }

                var completed = log.DecodeEvent<SwapCompletedEventDTO>();
                if (completed != null)
                {
                    await HandleSwapCompleted(log, completed, NetworkName);
                    //UpdateSwapLastBlock(log);
                    return;
                }

                var refunded = log.DecodeEvent<SwapRefundedEventDTO>();
                if (refunded != null)
                {
                    await HandleSwapRefunded(log, refunded, NetworkName);
                    //UpdateSwapLastBlock(log);
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

            lock (_blockLock)
            {
                _swapLastProcessedBlock = BigInteger.Max(_swapLastProcessedBlock, log.BlockNumber.Value + 1);
            }
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

        private async Task HandleSwapCompleted(FilterLog log, EventLog<SwapCompletedEventDTO> ev, string network)
        {
            var swapId = ByteArray32ToHex(ev.Event.SwapId);

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

        private async Task<HexBigInteger> GetSwapLastProcessedBlock(CancellationToken cancellationToken)
        {
            try
            {
                lock (_blockLock)
                {
                    if (_swapLastProcessedBlock > 0)
                        return _swapLastProcessedBlock.ToHexBigInteger();
                }

                var lastDbBlock = await _transactionLogService.GetSwapLastCheckedBlockNumberAsync(NetworkName);

                lock (_blockLock)
                {
                    _swapLastProcessedBlock = lastDbBlock;
                }

                if (_swapLastProcessedBlock > 0)
                    return _swapLastProcessedBlock.ToHexBigInteger();

                var _web3Client = new Web3(_blockChainSettings.BEP20RpcUrl);
                var latestBlockNumber = await _web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();

                lock (_blockLock)
                {
                    _swapLastProcessedBlock = latestBlockNumber;
                    return latestBlockNumber;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error getting swap last processed block");
                throw;
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




        #region Stake

        private async Task SubscribeToStakeContractEventsAsync(CancellationToken cancellationToken)
        {
            var subscription = new EthLogsObservableSubscription(_webSocketClient);

            var safeObservable = subscription
                    .GetSubscriptionDataResponsesAsObservable()
                    .Where(log => log.Address.IsTheSameAddress(_blockChainSettings.StakeContractAddress))
                    .Select(log => Observable.FromAsync(() => ProcessStakeContractEventLogAsync(log, cancellationToken)))
                    .Concat();

            _stakeContractEventsSubscription = safeObservable.Subscribe(
                _ => { },
                ex =>
                {
                    _logger.LogError(
                        ex,
                        "{Prefix} Stake subscription error",
                        StakeLogPrefix
                    );
                },
                () =>
                {
                    _logger.LogWarning(
                        "{Prefix} Stake subscription completed",
                        StakeLogPrefix
                    );
                });

            var filter = new NewFilterInput
            {
                Address = new[] { _blockChainSettings.StakeContractAddress },
                FromBlock = new BlockParameter(await GetStakeLastProcessedBlock(cancellationToken))
            };

            await subscription.SubscribeAsync(filter);

            _logger.LogInformation("{Prefix} Stake subscription active: {Address}", StakeLogPrefix, _blockChainSettings.StakeContractAddress);
        }

        private async Task ProcessStakeContractEventLogAsync(FilterLog log, CancellationToken cancellationToken)
        {
            try
            {
                var depositCreated = log.DecodeEvent<DepositCreatedEventDTO>();

                if (depositCreated != null)
                {
                    await CreateDepositCreatedLogAsync(log, depositCreated, cancellationToken);

                    return;
                }

                var earlyWithdrawn = log.DecodeEvent<EarlyWithdrawnEventDTO>();

                if (earlyWithdrawn != null)
                {
                    await CreateEarlyWithdrawnLogAsync(log, earlyWithdrawn, cancellationToken);

                    return;
                }

                var profitWithdrawn = log.DecodeEvent<ProfitWithdrawnEventDTO>();

                if (profitWithdrawn != null)
                {
                    await CreateProfitWithdrawnLogAsync(log, profitWithdrawn, cancellationToken);

                    return;
                }

                var withdrawn = log.DecodeEvent<WithdrawnEventDTO>();

                if (withdrawn != null)
                {
                    await CreateWithdrawnLogAsync(log, withdrawn, cancellationToken);

                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{Prefix} Stake event decode failed",
                    StakeLogPrefix
                );
            }
        }

        private async Task CreateDepositCreatedLogAsync(FilterLog log, EventLog<DepositCreatedEventDTO> eLog, CancellationToken cancellationToken)
        {
            try
            {
                var depositId = ByteArray32ToHex(eLog.Event.DepositId);

                _logger.LogInformation(
                  "{prefix} DepositCreated | DepositId: {DepositId}, Depositor: {Depositor}, Token: {Token}",
                  StakeLogPrefix,
                  depositId,
                  eLog.Event.Depositor,
                  eLog.Event.Token
              );

                SentrySdk.CaptureMessage(
                   $"{StakeLogPrefix} DepositCreated | DepositId: {depositId}, Depositor: {eLog.Event.Depositor}"
               );

                await _transactionLogService.CreateDepositCreatedLogAsync(
                    new DepositCreatedLog
                    {
                        Hash = log.TransactionHash,
                        Address = log.Address,
                        BlockNumber = log.BlockNumber!.Value,
                        DepositId = depositId,
                        Depositor = eLog.Event.Depositor,
                        Token = eLog.Event.Token,
                        LockDuration = eLog.Event.LockDuration,
                        Principal = eLog.Event.Principal,
                        Profit = eLog.Event.Profit,
                        UnlocksAt = eLog.Event.UnlocksAt,
                        EventType = Domain.Collections.BlockchainEventType.DepositCreated,
                        Network = NetworkName
                    });

                _lastEventReceived = DateTime.UtcNow;

                lock (_blockLock)
                {
                    _stakeLastProcessedBlock = BigInteger.Max(_stakeLastProcessedBlock, log.BlockNumber.Value + 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} DepositCreated failed", StakeLogPrefix
                );

                throw;
            }
        }

        private async Task CreateEarlyWithdrawnLogAsync(FilterLog log, EventLog<EarlyWithdrawnEventDTO> eLog, CancellationToken cancellationToken)
        {
            try
            {
                var depositId = ByteArray32ToHex(eLog.Event.DepositId);
                _logger.LogInformation(
                  "{prefix} EarlyWithdrawn | DepositId: {DepositId}, Depositor: {Depositor}",
                  StakeLogPrefix,
                  depositId,
                  eLog.Event.Depositor
              );

                SentrySdk.CaptureMessage(
                    $"{StakeLogPrefix} EarlyWithdrawn | DepositId: {depositId}, Depositor: {eLog.Event.Depositor}"
                );
                await _transactionLogService.CreateEarlyWithdrawnLogAsync(
                    new EarlyWithdrawnLog
                    {
                        Hash = log.TransactionHash,
                        Address = log.Address,
                        BlockNumber = log.BlockNumber!.Value,
                        DepositId = depositId,
                        Depositor = eLog.Event.Depositor,
                        WithdrawAmount = eLog.Event.WithdrawAmount,
                        ProfitAmount = eLog.Event.ProfitAmount,
                        FinalPayoutAmount = eLog.Event.FinalPayoutAmount,
                        ClaimedProfitAmount = eLog.Event.ClaimedProfitAmount,
                        EventType = Domain.Collections.BlockchainEventType.EarlyWithdrawn,
                        Network = NetworkName
                    });

                _lastEventReceived = DateTime.UtcNow;

                //lock (_blockLock)
                //{
                //    _lastProcessedBlock = BigInteger.Max(
                //        _lastProcessedBlock,
                //        log.BlockNumber.Value + 1
                //    );
                //}
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} EarlyWithdrawn failed", StakeLogPrefix);

                throw;
            }
        }

        private async Task CreateProfitWithdrawnLogAsync(FilterLog log, EventLog<ProfitWithdrawnEventDTO> eLog, CancellationToken cancellationToken)
        {
            try
            {
                var depositId = ByteArray32ToHex(eLog.Event.DepositId);
                _logger.LogInformation(
                  "{prefix} ProfitWithdrawn | DepositId: {DepositId}, Depositor: {Depositor}, Profit: {Profit}",
                  StakeLogPrefix,
                  depositId,
                  eLog.Event.Depositor,
                  eLog.Event.Profit
              );

                SentrySdk.CaptureMessage(
                    $"{StakeLogPrefix} ProfitWithdrawn | DepositId: {depositId}, Depositor: {eLog.Event.Depositor}, Profit: {eLog.Event.Profit}"
                );

                await _transactionLogService.CreateProfitWithdrawnLogAsync(
                    new ProfitWithdrawnLog
                    {
                        Hash = log.TransactionHash,
                        Address = log.Address,
                        BlockNumber = log.BlockNumber!.Value,
                        DepositId = depositId,
                        Depositor = eLog.Event.Depositor,
                        Token = eLog.Event.Token,
                        Profit = eLog.Event.Profit,
                        EventType = Domain.Collections.BlockchainEventType.ProfitWithdrawn,
                        Network = NetworkName
                    });

                _lastEventReceived = DateTime.UtcNow;

                //lock (_blockLock)
                //{
                //    _lastProcessedBlock = BigInteger.Max(
                //        _lastProcessedBlock,
                //        log.BlockNumber.Value + 1
                //    );
                //}
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{Prefix} ProfitWithdrawn failed",
                    StakeLogPrefix
                );

                throw;
            }
        }

        private async Task CreateWithdrawnLogAsync(FilterLog log, EventLog<WithdrawnEventDTO> eLog, CancellationToken cancellationToken)
        {
            try
            {
                var depositId = ByteArray32ToHex(eLog.Event.DepositId);


                _logger.LogInformation(
                   "{prefix} Withdrawn | DepositId: {DepositId}, Depositor: {Depositor}",
                   StakeLogPrefix,
                   depositId,
                   eLog.Event.Depositor
               );

                SentrySdk.CaptureMessage(
                    $"{StakeLogPrefix} Withdrawn | DepositId: {depositId}, Depositor: {eLog.Event.Depositor}"
                );


                await _transactionLogService.CreateWithdrawnLogAsync(
                    new WithdrawnLog
                    {
                        Hash = log.TransactionHash,
                        Address = log.Address,
                        BlockNumber = log.BlockNumber!.Value,
                        DepositId = depositId,
                        Depositor = eLog.Event.Depositor,
                        Principal = eLog.Event.Principal,
                        Profit = eLog.Event.Profit,
                        TotalPayout = eLog.Event.TotalPayout,
                        EventType = Domain.Collections.BlockchainEventType.WithdrawnAll,
                        Network = NetworkName
                    });

                _lastEventReceived = DateTime.UtcNow;

                //lock (_blockLock)
                //{
                //    _lastProcessedBlock = BigInteger.Max(
                //        _lastProcessedBlock,
                //        log.BlockNumber.Value + 1
                //    );
                //}
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "{Prefix} Withdrawn failed",
                    StakeLogPrefix
                );

                throw;
            }
        }

        private async Task<HexBigInteger> GetStakeLastProcessedBlock(CancellationToken cancellationToken)
        {
            try
            {
                lock (_blockLock)
                {
                    if (_stakeLastProcessedBlock > 0)
                    {
                        return _stakeLastProcessedBlock.ToHexBigInteger();
                    }
                }

                var lastDbBlock = await _transactionLogService.GetDepositLastCheckedBlockNumberAsync(NetworkName);

                lock (_blockLock)
                {
                    _stakeLastProcessedBlock = lastDbBlock;
                }

                if (_stakeLastProcessedBlock > 0)
                {
                    return _stakeLastProcessedBlock.ToHexBigInteger();
                }

                var latestBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

                lock (_blockLock)
                {
                    _stakeLastProcessedBlock = latestBlock;

                    return latestBlock;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} GetStakeLastProcessedBlock failed", StakeLogPrefix);

                SwitchRpc();
                InitializeClients();

                throw;
            }
        }

        #endregion









        #region TrasferSide

        private async Task SubscribeToIncomingTransfersAsync(CancellationToken cancellationToken)
        {
            var tokenAddresses = _availableTokensSettings
                .Select(t => t.Address.ToLower())
                .ToList();

            var subscription = new EthLogsObservableSubscription(_webSocketClient);

            var safeObservable = subscription.GetSubscriptionDataResponsesAsObservable()
                .Where(log => tokenAddresses.Contains(log.Address.ToLower()))
                .Select(log => Observable.FromAsync(() => ProcessIncomingTransferLogAsync(log)))
                .Concat();

            var contractAddresses = new List<string>
            {
                _blockChainSettings.BEP20SwapContractAddress,
                _blockChainSettings.PreSaleContractAddress,
                _blockChainSettings.StakeContractAddress
            };


            _incomingTransferSubscription = safeObservable.Subscribe(
                _ => { },
                async ex =>
                {
                    _logger.LogError(ex, "{Prefix} Incoming transfer subscription error", TransferLogPrefix);
                },
                () =>
                {
                    _logger.LogWarning("{Prefix} Incoming transfer subscription completed", TransferLogPrefix);
                });

            var filter = new NewFilterInput
            {
                Address = tokenAddresses.Concat(contractAddresses).ToArray()
            };

            await subscription.SubscribeAsync(filter);
        }

        private async Task ProcessIncomingTransferLogAsync(FilterLog log)
        {
            try
            {
                _lastEventReceived = DateTime.UtcNow;

                var transferEvent = log.DecodeEvent<TransferEventDTO>();
                if (transferEvent == null) return;

                var to = transferEvent.Event.To;
                var token = _availableTokensSettings.FirstOrDefault(t => t.Address.IsTheSameAddress(log.Address));

                if (token == null)
                {
                    _logger.LogWarning("{Prefix} Token not found: {Address}", TransferLogPrefix, log.Address);
                    return;
                }

                var amount = Web3.Convert.FromWei(transferEvent.Event.Value);

                if (to.IsTheSameAddress(_blockChainSettings.PreSaleContractAddress))
                {

                    _logger.LogInformation("PreSale Side {Token} Transfer: {Amount} from {From}", token.Name, amount, transferEvent.Event.From);
                    await _preSaleService.SyncPreSaleTokenBalanceAsync(token.Name.ToUpper());

                }
                else if (to.IsTheSameAddress(_blockChainSettings.BEP20SwapContractAddress))
                {
                    _logger.LogInformation("Swap Side: {Token} {Amount} from {From}", token.Name, amount, transferEvent.Event.From);
                    await _swapService.UpdateSingleTokenInStorageAsync(token.Address, token.Network);
                }
                else if (to.IsTheSameAddress(_blockChainSettings.StakeContractAddress))
                {
                    _logger.LogInformation("Stake Side : {Token} {Amount} from {From}", token.Name, amount, transferEvent.Event.From);
                    //TODO : complete
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} Error processing incoming token transfer", TransferLogPrefix);
            }
        }

        #endregion



        private static string ByteArray32ToHex(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (bytes.Length != 32)
                throw new ArgumentException("Input must be exactly 32 bytes for bytes32");

            return "0x" + bytes.ToHex();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_isDisposed) return;

            _logger.LogInformation("Shutting down blockchain event service...");

            try
            {
                await CleanupConnection();
            }
            finally
            {
                _isDisposed = true;
                await base.StopAsync(cancellationToken);
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _preSaleContractEventsSubscription?.Dispose();
                _swapContractEventsSubscription?.Dispose();
                _stakeContractEventsSubscription?.Dispose();
                _incomingTransferSubscription?.Dispose();
                _webSocketClient?.Dispose();
                _reconnectLock?.Dispose();
                _cleanupLock?.Dispose();
                _isDisposed = true;
            }
        }
    }
}


//var preSaleReleaseClaimedEvent = log.DecodeEvent<ClaimedEventDTO>();
//if (preSaleReleaseClaimedEvent != null)
//{
//    await CreatePreSaleOrderReleaseClaimedLogAsync(log, preSaleReleaseClaimedEvent);
//    return;
//}



//private async Task CreatePreSaleOrderReleaseClaimedLogAsync(FilterLog log, EventLog<ClaimedEventDTO> claimedEvent)
//{
//    var saleId = ByteArray32ToHex(claimedEvent.Event.SaleId);
//    var orderId = ByteArray32ToHex(claimedEvent.Event.OrderId);

//    _logger.LogInformation(
//        "****************************** Claimed: SaleId: {SaleId}, OrderId: {OrderId}, Buyer: {Buyer}, Amount: {Amount}",
//        saleId,
//        orderId,
//        claimedEvent.Event.Buyer,
//        claimedEvent.Event.AmountClaimed
//    );

//    SentrySdk.CaptureMessage(
//        $"****************************** Claimed: SaleId: {saleId}, OrderId: {orderId}, Buyer: {claimedEvent.Event.Buyer}, Amount: {claimedEvent.Event.AmountClaimed}"
//    );

//    await _transactionLogService.CreatePreSaleReleaseClaimedLogAsync(new PreSaleReleaseClaimedLog
//    {
//        Hash = log.TransactionHash,
//        Address = log.Address,
//        BlockNumber = log.BlockNumber.Value,

//        Buyer = claimedEvent.Event.Buyer,
//        SaleId = saleId,
//        OrderId = orderId,
//        AmountClaimed = claimedEvent.Event.AmountClaimed.ToString(),

//        EventType = BlockchainEventType.PreSaleReleaseClaimed
//    });

//    lock (_blockLock)
//    {
//        _preSaleLastProcessedBlock = BigInteger.Max(_preSaleLastProcessedBlock, log.BlockNumber.Value + 1);
//    }
//}
