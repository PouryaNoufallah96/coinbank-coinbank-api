using AutoShield.Services._BlockChain.DTOs.Settings;
using AutoShield.Services._TransactionLog;
using CoinBank.Services._BlockChainWebSocket.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using System.Numerics;
using static AutoShield.Utilities.Constants.RegisterMode;

namespace CoinBank.Services._BlockChainWebSocket
{
    public class PollingEventBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PollingEventBackgroundService> _logger,
        BlockChainSettings _settings
    ) : BackgroundService, IHostedDependency
    {
        private BigInteger _lastProcessedBlock = 0;
        private readonly object _blockLock = new object();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("------------------ Polling missing logs before subscription restart...");

            _lastProcessedBlock = await GetLastProcessedBlock(stoppingToken);
            _logger.LogInformation($"starting block is : {_lastProcessedBlock}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollMissingLogsAsync(stoppingToken);
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

        private async Task<HexBigInteger> GetLastProcessedBlock(CancellationToken cancellationToken)
        {
            lock (_blockLock)
            {
                if (_lastProcessedBlock > 0)
                    return _lastProcessedBlock.ToHexBigInteger();
            }

            using var scope = scopeFactory.CreateScope();
            var transactionLogService = scope.ServiceProvider.GetRequiredService<ITransactionLogService>();

            var lastDbBlock = await transactionLogService.GetLastCheckedBlockNumberAsync(cancellationToken);

            lock (_blockLock)
            {
                _lastProcessedBlock = lastDbBlock;
            }

            if (_lastProcessedBlock > 0)
                return _lastProcessedBlock.ToHexBigInteger();

            var _web3Client = new Web3(_settings.RpcUrl);
            var latestBlockNumber = await _web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();

            lock (_blockLock) 
            {
                _lastProcessedBlock = latestBlockNumber;
                return latestBlockNumber;
            }
        }

        private async Task PollMissingLogsAsync(CancellationToken cancellationToken)
        {
            var _web3Client = new Web3(_settings.RpcUrl);
            BigInteger latestBlock = await _web3Client.Eth.Blocks.GetBlockNumber.SendRequestAsync();

            if (_lastProcessedBlock >= latestBlock) return;

            const int blockChunk = 2000;
            BigInteger fromBlock = _lastProcessedBlock;

            using var scope = scopeFactory.CreateScope();
            var transactionLogService = scope.ServiceProvider.GetRequiredService<ITransactionLogService>();

            while (fromBlock <= latestBlock)
            {
                BigInteger toBlock = BigInteger.Min(fromBlock + blockChunk - 1, latestBlock);

                var filter = new NewFilterInput
                {
                    FromBlock = new BlockParameter(new HexBigInteger(fromBlock)),
                    ToBlock = new BlockParameter(new HexBigInteger(toBlock)),
                    Address = new[] { _settings.ContractAddress }
                };

                try
                {
                    var logs = await _web3Client.Eth.Filters.GetLogs.SendRequestAsync(filter);

                  

                    foreach (var log in logs)
                    {
                        var filterLog = log as FilterLog;
                        if (filterLog == null) continue;

                        try
                        {
                            var registeredEvent = log.DecodeEvent<InsuranceRegisteredEventDTO>();
                            if (registeredEvent != null)
                            {
                                await CreateRegisteredLog(transactionLogService, log, registeredEvent, cancellationToken);
                                continue;
                            }

                            var finalizedEvent = log.DecodeEvent<InsuranceFinalizedEventDTO>();
                            if (finalizedEvent != null)
                            {
                                await CreateFinalizedLog(transactionLogService, log, finalizedEvent, cancellationToken);
                                continue;
                            }

                            var cancelledEvent = log.DecodeEvent<InsuranceCancelledEventDTO>();
                            if (cancelledEvent != null)
                            {
                                await CreateCancelledLog(transactionLogService, log, cancelledEvent, cancellationToken);
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
                await Task.Delay(3000);
            }

            lock (_blockLock)
            {
                _lastProcessedBlock = BigInteger.Max(_lastProcessedBlock, latestBlock);
                _logger.LogInformation("-------------- Poling until {latestBlock}", latestBlock);
            }
        }

        private async Task CreateRegisteredLog(ITransactionLogService transactionLogService, FilterLog log, EventLog<InsuranceRegisteredEventDTO> registered, CancellationToken cancellationToken)
        {
            var shieldRef = ByteArray32ToHex(registered.Event.InsuranceId);
            _logger.LogInformation(
                "***************************** InsuranceRegistered: {InsuranceId}, User: {User}, Token: {Token}, Coverage: {Coverage}",
                shieldRef,
                registered.Event.User,
                registered.Event.InsuredToken,
                registered.Event.CoverageAmount
            );

            SentrySdk.CaptureMessage(
                $"****************************** InsuranceRegistered: {shieldRef}, User: {registered.Event.User}, Token: {registered.Event.InsuredToken}, Coverage: {registered.Event.CoverageAmount}"
            );

            await transactionLogService.CreateInsuranceRegisteredLogAsync(new _TransactionLog.DTOs.Updates.InsuranceRegisteredLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                UserWallet = registered.Event.User,
                CoverageAmount = registered.Event.CoverageAmount.ToString(),
                EventType = Domain.Entities.BlockchainEventType.InsuranceRegistered,
                InsuredTokenAddress = registered.Event.InsuredToken,
                ShieldReference = shieldRef,
            }, cancellationToken);
        }

        private async Task CreateFinalizedLog(ITransactionLogService transactionLogService, FilterLog log, EventLog<InsuranceFinalizedEventDTO> finalizedEvent, CancellationToken cancellationToken)
        {
            var shieldRef = ByteArray32ToHex(finalizedEvent.Event.InsuranceId);
            _logger.LogInformation(
                "****************************** InsuranceFinalized: {InsuranceId}, User: {User}, SettlementAmount: {Settlement}, FinalPrice: {FinalPrice}",
                shieldRef,
                finalizedEvent.Event.User,
                finalizedEvent.Event.SettlementAmount,
                finalizedEvent.Event.FinalPrice
            );


            SentrySdk.CaptureMessage(
                $"****************************** InsuranceFinalized: {shieldRef}, User: {finalizedEvent.Event.User}, SettlementAmount: {finalizedEvent.Event.SettlementAmount}, FinalPrice: {finalizedEvent.Event.FinalPrice}"
            );

            await transactionLogService.CreateInsuranceFinalizedLogAsync(new _TransactionLog.DTOs.Updates.InsuranceFinalizedLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                UserWallet = finalizedEvent.Event.User,
                EventType = Domain.Entities.BlockchainEventType.InsuranceFinalized,
                ShieldReference = shieldRef,
                FinalPrice = (decimal)finalizedEvent.Event.FinalPrice,
                SettlementAmount = Web3.Convert.FromWei(finalizedEvent.Event.SettlementAmount),
                PayoutAmount = Web3.Convert.FromWei(finalizedEvent.Event.PayoutAmount),
                PayoutToken = finalizedEvent.Event.PayoutToken,
            }, cancellationToken);
        }

        private async Task CreateCancelledLog(ITransactionLogService transactionLogService, FilterLog log, EventLog<InsuranceCancelledEventDTO> cancelledEvent, CancellationToken cancellationToken)
        {
            var shieldRef = ByteArray32ToHex(cancelledEvent.Event.InsuranceId);

            _logger.LogInformation(
                "****************************** InsuranceCancelled: {InsuranceId}, User: {User}",
                shieldRef,
                cancelledEvent.Event.User
            );
            SentrySdk.CaptureMessage(
                $"****************************** InsuranceCancelled: {shieldRef}, User: {cancelledEvent.Event.User}"
            );
            await transactionLogService.CreateInsuranceCancelledLogAsync(new _TransactionLog.DTOs.Updates.InsuranceCancelledLog
            {
                Hash = log.TransactionHash,
                Address = log.Address,
                BlockNumber = log.BlockNumber.Value,
                UserWallet = cancelledEvent.Event.User,
                EventType = Domain.Entities.BlockchainEventType.InsuranceCancelled,
                ShieldReference = shieldRef
            }, cancellationToken);
        }

        private static string ByteArray32ToHex(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (bytes.Length != 32)
                throw new ArgumentException("Input must be exactly 32 bytes for bytes32");

            return "0x" + bytes.ToHex();
        }

        private HexBigInteger GetLastProcessedBlock()
        {
            lock (_blockLock)
            {
                return _lastProcessedBlock.ToHexBigInteger();
            }
        }

        private async Task<BigInteger> GetLatestBlockSafeAsync(Web3 web3, int retries = 3)
        {
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    var result = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                    return result.Value;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error getting latest block (attempt {Attempt})", i + 1);
                    await Task.Delay(2000);
                }
            }

            throw new Exception("Failed to retrieve latest block after retries");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Shutting down blockchain Polling service...");
            await base.StopAsync(cancellationToken);
        }
    }
}
