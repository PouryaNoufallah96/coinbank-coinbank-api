
using CoinBank.Domain.Collections;
using CoinBank.Services._BlockChain.DTOs.Settings;
using CoinBank.Services._BlockChainWebSocket.DTOs;
using CoinBank.Services._Transaction;
using CoinBank.Services._Transaction.DTOs.Updates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using System.Numerics;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._BlockChainWebSocket
{
    public class PollingEventBackgroundService : BackgroundService, IHostedDependency
    {
        private const string LogPrefix = "[BEP20-POLLING]";
        private readonly ITransactionLogService _transactionLogService;
        private readonly ILogger<PollingEventBackgroundService> _logger;
        private readonly BlockChainSettings _blockChainSettings;
        private readonly object _blockLock = new();
        private readonly Web3 _web3;
        private BigInteger _swapLastProcessedBlock = 0;
        private BigInteger _preSaleLastProcessedBlock = 0;

        public PollingEventBackgroundService(
            ITransactionLogService transactionLogService,
            ILogger<PollingEventBackgroundService> logger, BlockChainSettings blockChainSettings)
        {
            _transactionLogService = transactionLogService; _logger = logger;
            _blockChainSettings = blockChainSettings;
            _web3 = new Web3(_blockChainSettings.BEP20RpcUrl);
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    BigInteger latestBlock = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                    _logger.LogInformation(
                     "{Prefix} Checking latest block: {Block}",
                     LogPrefix,
                     latestBlock);

                    var safeBlock = latestBlock - 10;
                    await PollPresaleMissingLogsAsync(safeBlock, stoppingToken);
                    await PollSwapMissingLogsAsync(safeBlock, stoppingToken);

                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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


        #region PreSale

        private async Task PollPresaleMissingLogsAsync(BigInteger latestBlock, CancellationToken cancellationToken)
        {

            if (_preSaleLastProcessedBlock < 1)
            {
                _preSaleLastProcessedBlock = await GetPreSaleOrderLastProcessedBlock(cancellationToken);
            }

            if (_preSaleLastProcessedBlock >= latestBlock) return;

            const int blockChunk = 2000;
            BigInteger fromBlock = _preSaleLastProcessedBlock;


            while (fromBlock <= latestBlock)
            {
                BigInteger toBlock = BigInteger.Min(fromBlock + blockChunk - 1, latestBlock);

                var filter = new NewFilterInput
                {
                    FromBlock = new BlockParameter(new HexBigInteger(fromBlock)),
                    ToBlock = new BlockParameter(new HexBigInteger(toBlock)),
                    Address = new[] { _blockChainSettings.PreSaleContractAddress }
                };

                try
                {
                    var logs = await _web3.Eth.Filters.GetLogs.SendRequestAsync(filter);

                    foreach (var log in logs)
                    {
                        var filterLog = log as FilterLog;
                        if (filterLog == null) continue;

                        try
                        {

                            var preSaleOrderRegisteredEvent = log.DecodeEvent<PurchasedEventDTO>();
                            if (preSaleOrderRegisteredEvent != null)
                            {
                                await CreatePreSaleOrderCreateLogAsync(log, preSaleOrderRegisteredEvent);
                                continue;
                            }

                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error decoding polled log");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error polling logs from {FromBlock} to {ToBlock}", fromBlock, toBlock);
                }

                fromBlock = toBlock + 1;
                await Task.Delay(3000, cancellationToken);
            }

            lock (_blockLock)
            {
                _preSaleLastProcessedBlock = BigInteger.Max(_preSaleLastProcessedBlock, latestBlock);
                _logger.LogInformation(
                 "{Prefix} Checking latest block: {Block}",
                 LogPrefix,
                 latestBlock);
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

                    var latestBlockNumber = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

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

        private async Task PollSwapMissingLogsAsync(BigInteger latestBlock, CancellationToken cancellationToken)
        {

            if (_swapLastProcessedBlock < 1)
            {
                _swapLastProcessedBlock = await GetSwapLastProcessedBlock(cancellationToken);

            }

            if (_swapLastProcessedBlock >= latestBlock) return;

            const int blockChunk = 2000;
            BigInteger fromBlock = _swapLastProcessedBlock;

            while (fromBlock <= latestBlock)
            {
                BigInteger toBlock = BigInteger.Min(fromBlock + blockChunk - 1, latestBlock);

                var filter = new NewFilterInput
                {
                    FromBlock = new BlockParameter(new HexBigInteger(fromBlock)),
                    ToBlock = new BlockParameter(new HexBigInteger(toBlock)),
                    Address = new[] { _blockChainSettings.BEP20SwapContractAddress }
                };

                try
                {
                    var logs = await _web3.Eth.Filters.GetLogs.SendRequestAsync(filter);

                    foreach (var log in logs)
                    {
                        var filterLog = log as FilterLog;
                        if (filterLog == null) continue;

                        try
                        {

                            var initiated = log.DecodeEvent<SwapInitiatedEventDTO>();
                            if (initiated != null)
                            {
                                await HandleSwapInitiated(log, initiated, "BEP20");
                                continue;
                            }

                            var executed = log.DecodeEvent<SwapExecutedEventDTO>();
                            if (executed != null)
                            {
                                await HandleSwapExecuted(log, executed, "BEP20");
                                continue;
                            }

                            var failed = log.DecodeEvent<SwapFailedEventDTO>();
                            if (failed != null)
                            {
                                await HandleSwapFailed(log, failed, "BEP20");
                                continue;
                            }

                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error decoding polled log");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error polling logs from {FromBlock} to {ToBlock}", fromBlock, toBlock);
                }

                fromBlock = toBlock + 1;
                await Task.Delay(3000, cancellationToken);
            }

            lock (_blockLock)
            {
                _swapLastProcessedBlock = BigInteger.Max(_swapLastProcessedBlock, latestBlock);
                _logger.LogInformation(
                 "{Prefix} Checking latest block: {Block}",
                 LogPrefix,
                 latestBlock);
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
                _swapLastProcessedBlock =
                    BigInteger.Max(_swapLastProcessedBlock, log.BlockNumber.Value + 1);
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


                var latestBlockNumber = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

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
            _logger.LogInformation("Shutting down blockchain Polling service...");
            await base.StopAsync(cancellationToken);
        }
    }
}
