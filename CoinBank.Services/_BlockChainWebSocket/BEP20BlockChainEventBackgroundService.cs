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
        private const string LogPrefix = "[BEP20]";

        private readonly BlockChainSettings blockChainSettings;
        private readonly ITransactionLogService _transactionLogService;
        private readonly IPreSaleService _preSaleService;
        private readonly ILogger<BEP20BlockChainEventBackgroundService> _logger;
        private readonly AvailableTokensSettings _availableTokensSettings;
        private readonly ISwapService _swapService;
        private int _reconnectAttempts = 0;
        private DateTime _lastEventReceived = DateTime.UtcNow;

        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
        private readonly SemaphoreSlim _cleanupLock = new(1, 1);
        private readonly object _blockLock = new();

        private bool _useSecondaryWsUrl = false;
        private bool _isDisposed = false;

        private IDisposable _preSaleContractEventsSubscription;
        private IDisposable _incomingTransferSubscription;
        private IDisposable _swapContractEventsSubscription;

        private BigInteger _swapLastProcessedBlock = 0;
        private BigInteger _preSaleLastProcessedBlock = 0;

        private Web3 _web3;
        private StreamingWebSocketClient _webSocketClient;

        public BEP20BlockChainEventBackgroundService(
            BlockChainSettings blockChainSettings,
            ITransactionLogService transactionLogService,
            IPreSaleService preSaleService,
            AvailableTokensSettings availableTokensSettings,
            ISwapService swapService,
            ILogger<BEP20BlockChainEventBackgroundService> logger)
        {
            this.blockChainSettings = blockChainSettings;
            _transactionLogService = transactionLogService;
            _preSaleService = preSaleService;
            _availableTokensSettings = availableTokensSettings;
            _swapService = swapService;
            _logger = logger;

            _web3 = new Web3(blockChainSettings.BEP20WsUrl2);
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
                    _logger.LogInformation("{Prefix} Shutdown requested", LogPrefix);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Prefix} Unexpected error", LogPrefix);
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
                        _logger.LogInformation("{Prefix} Connecting attempt {Attempt}/{Max}",
                            LogPrefix,
                            _reconnectAttempts + 1,
                            blockChainSettings.MaxReconnectAttempts);

                        await ConnectAndSubscribe(stoppingToken);

                        _reconnectAttempts = 0;
                        return;
                    }
                    catch (Exception ex)
                    {
                        _reconnectAttempts++;

                        _logger.LogWarning(ex,
                            "{Prefix} Connection failed, retrying...",
                            LogPrefix);

                        await Task.Delay(CalculateReconnectDelay(), stoppingToken);
                    }
                }

                _logger.LogCritical("{Prefix} Max reconnect attempts reached", LogPrefix);
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
            double delaySeconds = Math.Min(
                Math.Pow(2, _reconnectAttempts) * blockChainSettings.ReconnectInterval,
                300);
            return TimeSpan.FromSeconds(delaySeconds);
        }

        private async Task ConnectAndSubscribe(CancellationToken cancellationToken)
        {
            _logger.LogInformation("{Prefix} Connecting WebSocket...", LogPrefix);

            await CleanupConnection();

            var url = GetCurrentWsUrl();

            _webSocketClient = new StreamingWebSocketClient(url);
            _web3 = new Web3(url);

            await _webSocketClient.StartAsync();

            await SubscribeToPreSaleContractEventsAsync(cancellationToken);
            await SubscribeToSwapContractEventsAsync(cancellationToken);
            await SubscribeToIncomingTransfersAsync(cancellationToken);

            _logger.LogInformation("{Prefix} Subscriptions active", LogPrefix);
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
                _preSaleContractEventsSubscription = null;
                _incomingTransferSubscription = null;
                _swapContractEventsSubscription = null;

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

        private string GetCurrentWsUrl()
        {
            var wss = _useSecondaryWsUrl ? blockChainSettings.BEP20WsUrl : blockChainSettings.BEP20WsUrl2;
            _useSecondaryWsUrl = !_useSecondaryWsUrl;
            _logger.LogInformation("WebSocket URL : {Url}", wss);
            return wss;
        }


        #region PreSaleSide
        private async Task SubscribeToPreSaleContractEventsAsync(CancellationToken cancellationToken)
        {
            var subscription = new EthLogsObservableSubscription(_webSocketClient);

            var safeObservable = subscription.GetSubscriptionDataResponsesAsObservable()
           .Where(log => log.Address.IsTheSameAddress(blockChainSettings.PreSaleContractAddress))
           .Select(log => Observable.FromAsync(() => PreSaleProcessContractEventLogAsync(log, cancellationToken)))
           .Concat();

            _preSaleContractEventsSubscription = safeObservable.Subscribe(
                _ => { },
                async ex =>
                {
                    _logger.LogError(ex, "Error in subscription. Reconnecting...");
                },
                () =>
                {
                    _logger.LogWarning("Subscription completed unexpectedly. Reconnecting...");
                });

            var filter = new NewFilterInput
            {
                Address = new[] { blockChainSettings.PreSaleContractAddress },
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

                //var preSaleReleaseClaimedEvent = log.DecodeEvent<ClaimedEventDTO>();
                //if (preSaleReleaseClaimedEvent != null)
                //{
                //    await CreatePreSaleOrderReleaseClaimedLogAsync(log, preSaleReleaseClaimedEvent);
                //    return;
                //}

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error decoding blockchain event");
            }
        }

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

        private async Task CreatePreSaleOrderCreateLogAsync(FilterLog log, EventLog<PurchasedEventDTO> purchasedEvent)
        {
            var saleId = ByteArray32ToHex(purchasedEvent.Event.SaleId);
            var orderId = ByteArray32ToHex(purchasedEvent.Event.OrderId);

            _logger.LogInformation(
                "****************************** Purchased: SaleId: {SaleId}, OrderId: {OrderId}, Buyer: {Buyer}, Purchased: {Purchased}, Paid: {Paid}",
                saleId,
                orderId,
                purchasedEvent.Event.Buyer,
                purchasedEvent.Event.AmountPurchased,
                purchasedEvent.Event.AmountPaid
            );

            SentrySdk.CaptureMessage(
                $"****************************** Purchased: SaleId: {saleId}, OrderId: {orderId}, Buyer: {purchasedEvent.Event.Buyer}, Purchased: {purchasedEvent.Event.AmountPurchased}, Paid: {purchasedEvent.Event.AmountPaid}"
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
                    var _web3Client = new Web3(blockChainSettings.BEP20RpcUrl);
                    var latestBlockNumber = await _web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();

                    lock (_blockLock)
                    {
                        _swapLastProcessedBlock = latestBlockNumber;
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
                blockChainSettings.BEP20SwapContractAddress
            };

            var observable = subscription.GetSubscriptionDataResponsesAsObservable()
                .Where(log => contracts.Any(c => log.Address.IsTheSameAddress(c)))
                .Select(log => Observable.FromAsync(() =>
                    SwapProcessContractEventLogAsync(log, cancellationToken)))
                .Concat();

            _swapContractEventsSubscription = observable.Subscribe(
                _ => { },
                ex => _logger.LogError(ex, "{Prefix} Swap subscription error", LogPrefix),
                () => _logger.LogWarning("{Prefix} Swap subscription closed", LogPrefix)
            );

            await subscription.SubscribeAsync(new NewFilterInput
            {
                Address = contracts,
                FromBlock = new BlockParameter(
                    await GetSwapLastProcessedBlock(cancellationToken))
            });
        }

        private async Task SwapProcessContractEventLogAsync(FilterLog log, CancellationToken cancellationToken)
        {
            try
            {
                _lastEventReceived = DateTime.UtcNow;

                var initiated = log.DecodeEvent<SwapInitiatedEventDTO>();
                if (initiated != null)
                {
                    await HandleSwapInitiated(log, initiated, "BEP20");
                    return;
                }

                var executed = log.DecodeEvent<SwapExecutedEventDTO>();
                if (executed != null)
                {
                    await HandleSwapExecuted(log, executed, "BEP20");
                    return;
                }

                var failed = log.DecodeEvent<SwapFailedEventDTO>();
                if (failed != null)
                {
                    await HandleSwapFailed(log, failed, "BEP20");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} Swap decode error", LogPrefix);
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

            //lock (_blockLock)
            //{
            //    _swapLastProcessedBlock = BigInteger.Max(_swapLastProcessedBlock, log.BlockNumber.Value + 1);
            //}
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

            //lock (_blockLock)
            //{
            //    _swapLastProcessedBlock = BigInteger.Max(_swapLastProcessedBlock, log.BlockNumber.Value + 1);
            //}
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

                var lastDbBlock = await _transactionLogService.GetSwapLastCheckedBlockNumberAsync("BEP20");

                lock (_blockLock)
                {
                    _swapLastProcessedBlock = lastDbBlock;
                }

                if (_swapLastProcessedBlock > 0)
                    return _swapLastProcessedBlock.ToHexBigInteger();

                var _web3Client = new Web3(blockChainSettings.BEP20RpcUrl);
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
                blockChainSettings.BEP20SwapContractAddress,
                blockChainSettings.PreSaleContractAddress,
                blockChainSettings.StakeContractAddress
            };


            _incomingTransferSubscription = safeObservable.Subscribe(
                _ => { },
                async ex =>
                {
                    _logger.LogError(ex, "Error in incoming transfer subscription. Reconnecting...");
                    //_ = Task.Run(async () => await TryConnectWithRetryAsync(cancellationToken));
                },
                () =>
                {
                    _logger.LogWarning("Incoming transfer subscription completed unexpectedly. Reconnecting...");
                    //_ = Task.Run(async () => await TryConnectWithRetryAsync(cancellationToken));
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
                    _logger.LogWarning("Token not found for address {Address}", log.Address);
                    return;
                }

                var amount = Web3.Convert.FromWei(transferEvent.Event.Value);

                if (to.IsTheSameAddress(blockChainSettings.PreSaleContractAddress))
                {

                    _logger.LogInformation("PreSale Side {Token} Transfer: {Amount} from {From}", token.Name, amount, transferEvent.Event.From);
                    await _preSaleService.SyncPreSaleTokenBalanceAsync(token.Name.ToUpper());

                }
                else if (to.IsTheSameAddress(blockChainSettings.BEP20SwapContractAddress))
                {
                    _logger.LogInformation("Swap Side: {Token} {Amount} from {From}", token.Name, amount, transferEvent.Event.From);
                    await _swapService.UpdateSingleTokenInStorageAsync(token.Address, token.Network);
                }
                else if (to.IsTheSameAddress(blockChainSettings.StakeContractAddress))
                {
                    _logger.LogInformation("Stake Side : {Token} {Amount} from {From}", token.Name, amount, transferEvent.Event.From);
                    //TODO : complete
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing incoming token transfer");
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
                _incomingTransferSubscription?.Dispose();
                _webSocketClient?.Dispose();
                _reconnectLock?.Dispose();
                _cleanupLock?.Dispose();
                _isDisposed = true;
            }
        }
    }
}
