using CoinBank.Domain.Collections;
using CoinBank.Services._BlockChain.DTOs.Settings;
using CoinBank.Services._BlockChainWebSocket.DTOs;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._PreSale;
using CoinBank.Services._Transaction;
using CoinBank.Services._Transaction.DTOs.Updates;
using Microsoft.Extensions.DependencyInjection;
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
    public class BlockChainEventBackgroundService : BackgroundService, IHostedDependency
    {
        private readonly BlockChainSettings blockChainSettings;
        private readonly ITransactionLogService _transactionLogService;
        private readonly IPreSaleService _preSaleService;
        private readonly ILogger<BlockChainEventBackgroundService> _logger;
        private readonly BlockchainWebSocketSetting _settings;
        private BigInteger _preSaleLastProcessedBlock = 0;
        private int _reconnectAttempts = 0;
        private DateTime _lastEventReceived = DateTime.UtcNow;
        private readonly SemaphoreSlim _reconnectLock = new(1, 1);
        private bool _isCleaningUp = false;
        private readonly SemaphoreSlim _cleanupLock = new(1, 1);
        private IDisposable _preSaleContractEventsSubscription;
        private IDisposable _incomingTransferSubscription;

        private bool _useSecondaryWsUrl = false;
        private Web3 _web3;
        private StreamingWebSocketClient _webSocketClient;
        private readonly object _blockLock = new();
        private bool _isDisposed = false;
        private readonly AvailableTokensSettings _availableTokensSettings;


        public BlockChainEventBackgroundService(
            BlockChainSettings blockChainSettings,
            ITransactionLogService transactionLogService,
            IPreSaleService preSaleService,
            AvailableTokensSettings availableTokensSettings,
            ILogger<BlockChainEventBackgroundService> logger,
            BlockchainWebSocketSetting settings)
        {
            this.blockChainSettings = blockChainSettings;
            _transactionLogService = transactionLogService;
            _preSaleService = preSaleService;
            _logger = logger;
            _settings = settings;
            _availableTokensSettings = availableTokensSettings;
            _web3 = new Web3(settings.WsUrl2);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Blockchain Event Service starting...");
            _preSaleLastProcessedBlock = await GetPreSaleOrderLastProcessedBlock(stoppingToken);
            _logger.LogInformation($"PreSale starting block is : {_preSaleLastProcessedBlock}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TryConnectWithRetryAsync(stoppingToken);
                    _lastEventReceived = DateTime.UtcNow;

                    _logger.LogInformation("-----------------------Successfully connected and subscribed to blockchain events");

                    while (_webSocketClient?.IsStarted == true && !stoppingToken.IsCancellationRequested)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _lastEventReceived).TotalMinutes > 2)
                        {
                            _logger.LogWarning("----------- No blockchain events received in the last 3 minutes {time}. Reconnecting...", now);
                            await Task.Delay(2000, stoppingToken);
                            await TryConnectWithRetryAsync(stoppingToken);

                            _lastEventReceived = DateTime.UtcNow;
                        }

                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }

                    if (_webSocketClient != null && !_webSocketClient.IsStarted)
                    {
                        _logger.LogWarning("WebSocket stopped unexpectedly, reconnecting...");
                        await Task.Delay(2000, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Service shutdown requested");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in blockchain event service");
                    await Task.Delay(5000, stoppingToken);
                }
            }

            _logger.LogInformation("Blockchain Event Service stopped.");
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

                while (!stoppingToken.IsCancellationRequested &&
                       _reconnectAttempts < _settings.MaxReconnectAttempts)
                {
                    try
                    {
                        _logger.LogInformation($"Attempting to connect (Attempt {_reconnectAttempts + 1}/{_settings.MaxReconnectAttempts})");
                        await ConnectAndSubscribe(stoppingToken);

                        _reconnectAttempts = 0;
                        return;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _reconnectAttempts++;
                        _logger.LogWarning(ex, "Connection attempt failed. Will retry...");
                        await Task.Delay(CalculateReconnectDelay(), stoppingToken);
                    }
                }

                if (_reconnectAttempts >= _settings.MaxReconnectAttempts)
                {
                    _logger.LogCritical("Max reconnection attempts reached. Waiting before next try...");
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
            double delaySeconds = Math.Min(
                Math.Pow(2, _reconnectAttempts) * _settings.ReconnectInterval,
                300);
            return TimeSpan.FromSeconds(delaySeconds);
        }


        private async Task ConnectAndSubscribe(CancellationToken cancellationToken)
        {
            _logger.LogInformation("...........ConnectAndSubscribe touched............");

            await CleanupConnection();

            var currestWsUrl = GetCurrentWsUrl();
            _webSocketClient = new StreamingWebSocketClient(currestWsUrl);
            _web3 = new Web3(currestWsUrl);


            try
            {
                await _webSocketClient.StartAsync();

                await SubscribeToPreSaleContractEventsAsync(cancellationToken);

                await SubscribeToIncomingTransfersAsync(cancellationToken);

                _logger.LogInformation("ContractEvents subscription is active.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting/subscribing. Will reconnect...");
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
                _preSaleContractEventsSubscription = null;
                _incomingTransferSubscription = null;

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
            var wss = _useSecondaryWsUrl ? _settings.WsUrl : _settings.WsUrl2;
            _useSecondaryWsUrl = !_useSecondaryWsUrl;
            _logger.LogInformation("WebSocket URL : {Url}", wss);
            return wss;
        }



        #region PreSaleSide
        private async Task SubscribeToPreSaleContractEventsAsync(CancellationToken cancellationToken)
        {
            var subscription = new EthLogsObservableSubscription(_webSocketClient);

            var safeObservable = subscription.GetSubscriptionDataResponsesAsObservable()
           .Where(log => log.Address.IsTheSameAddress(_settings.PreSaleContractAddress))
           .Select(log => Observable.FromAsync(() => ProcessContractEventLogAsync(log, cancellationToken)))
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
                Address = new[] { _settings.PreSaleContractAddress },
                FromBlock = new BlockParameter(await GetPreSaleOrderLastProcessedBlock(cancellationToken))
            };

            await subscription.SubscribeAsync(filter);
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
                    var _web3Client = new Web3(blockChainSettings.RpcUrl);

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

        private async Task ProcessContractEventLogAsync(FilterLog log, CancellationToken cancellationToken)
        {
            try
            {
                var preSaleOrderRegisteredEvent = log.DecodeEvent<PurchasedEventDTO>();
                if (preSaleOrderRegisteredEvent != null)
                {
                    await CreatePreSaleOrderCreateLogAsync(log, preSaleOrderRegisteredEvent);
                    return;
                }

                var preSaleReleaseClaimedEvent = log.DecodeEvent<ClaimedEventDTO>();
                if (preSaleReleaseClaimedEvent != null)
                {
                    await CreatePreSaleOrderReleaseClaimedLogAsync(log, preSaleReleaseClaimedEvent);
                    return;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error decoding blockchain event");
            }
        }

        private async Task CreatePreSaleOrderReleaseClaimedLogAsync(FilterLog log, EventLog<ClaimedEventDTO> claimedEvent)
        {
            var saleId = ByteArray32ToHex(claimedEvent.Event.SaleId);
            var orderId = ByteArray32ToHex(claimedEvent.Event.OrderId);

            _logger.LogInformation(
                "****************************** Claimed: SaleId: {SaleId}, OrderId: {OrderId}, Buyer: {Buyer}, Amount: {Amount}",
                saleId,
                orderId,
                claimedEvent.Event.Buyer,
                claimedEvent.Event.AmountClaimed
            );

            SentrySdk.CaptureMessage(
                $"****************************** Claimed: SaleId: {saleId}, OrderId: {orderId}, Buyer: {claimedEvent.Event.Buyer}, Amount: {claimedEvent.Event.AmountClaimed}"
            );

            await _transactionLogService.CreatePreSaleReleaseClaimedLogAsync(new PreSaleReleaseClaimedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,

                Buyer = claimedEvent.Event.Buyer,
                SaleId = saleId,
                OrderId = orderId,
                AmountClaimed = claimedEvent.Event.AmountClaimed.ToString(),

                EventType = BlockchainEventType.PreSaleReleaseClaimed
            });

            lock (_blockLock)
            {
                _preSaleLastProcessedBlock = BigInteger.Max(_preSaleLastProcessedBlock, log.BlockNumber.Value + 1);
            }
        }

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
                _settings.SwapContractAddress,
                _settings.PreSaleContractAddress,
                _settings.StakeContractAddress
            };


            _incomingTransferSubscription = safeObservable.Subscribe(
                _ => { },
                async ex =>
                {
                    _logger.LogError(ex, "Error in incoming transfer subscription. Reconnecting...");
                    _ = Task.Run(async () => await TryConnectWithRetryAsync(cancellationToken));
                },
                () =>
                {
                    _logger.LogWarning("Incoming transfer subscription completed unexpectedly. Reconnecting...");
                    _ = Task.Run(async () => await TryConnectWithRetryAsync(cancellationToken));
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

                if (to.IsTheSameAddress(_settings.PreSaleContractAddress))
                {

                    _logger.LogInformation("PreSale Side {Token} Transfer: {Amount} from {From}", token.Name, amount, transferEvent.Event.From);
                    await _preSaleService.SyncPreSaleTokenBalanceAsync(token.Name.ToUpper());

                }
                else if (to.IsTheSameAddress(_settings.SwapContractAddress))
                {
                    _logger.LogInformation("Swap Side: {Token} {Amount} from {From}", token.Name, amount, transferEvent.Event.From);
                    //TODO : complete

                }
                else if (to.IsTheSameAddress(_settings.StakeContractAddress))
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



        private async Task<HexBigInteger> GetLastProcessedBlock(CancellationToken cancellationToken)
        {

            try
            {


                lock (_blockLock)
                {
                    if (_preSaleLastProcessedBlock > 0)
                        return _preSaleLastProcessedBlock.ToHexBigInteger();
                }

                var lastDbBlock = await _transactionLogService.GetLastCheckedBlockNumberAsync();

                lock (_blockLock)
                {
                    _preSaleLastProcessedBlock = lastDbBlock;
                }

                if (_preSaleLastProcessedBlock > 0)
                    return _preSaleLastProcessedBlock.ToHexBigInteger();

                try
                {
                    var _web3Client = new Web3(blockChainSettings.RpcUrl);

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
                _webSocketClient?.Dispose();
                _reconnectLock?.Dispose();
                _cleanupLock?.Dispose();
                _isDisposed = true;
            }
        }
    }
}
