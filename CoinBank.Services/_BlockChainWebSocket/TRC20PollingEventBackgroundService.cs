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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._BlockChainWebSocket
{
    public class TRC20PollingEventBackgroundService : BackgroundService, IHostedDependency
    {
        private const string SwapLogPrefix = "[TRC20-POLLING-Swap]";
        private const string CommonLogPrefix = "[TRC20-POLLING]";
        private const string NetworkName = "TRC20";

        private readonly ITransactionLogService _transactionLogService;
        private readonly ILogger<TRC20PollingEventBackgroundService> _logger;
        private readonly BlockChainSettings _blockChainSettings;
        private readonly object _blockLock = new();

        private Web3 _web3;

        private readonly string[] _rpcUrls;

        private int _currentRpcIndex = 0;

        private BigInteger _swapLastProcessedBlock = 0;
        private readonly string _swapContractAddress;

        private bool _isDisposed = false;

        public TRC20PollingEventBackgroundService(
            ITransactionLogService transactionLogService,
            ILogger<TRC20PollingEventBackgroundService> logger,
            BlockChainSettings blockChainSettings)
        {
            _transactionLogService = transactionLogService;
            _logger = logger;
            _blockChainSettings = blockChainSettings;

            _rpcUrls = new[] { _blockChainSettings.TRC20RpcUrl };
            _swapContractAddress = _blockChainSettings.TRC20SwapContractAddress;

            InitializeClients();
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
                     CommonLogPrefix,
                     latestBlock);

                    var safeBlock = latestBlock - 10;
                    await PollSwapMissingLogsAsync(safeBlock, stoppingToken);

                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Service shutdown requested");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Prefix} Unexpected error in blockchain event service", CommonLogPrefix);
                    await Task.Delay(5000, stoppingToken);
                }
            }

            _logger.LogInformation("Blockchain Event Service stopped.");
        }


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
                    Address = new[] { _swapContractAddress }
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
                                await HandleSwapInitiated(log, initiated, NetworkName);
                                continue;
                            }

                            var executed = log.DecodeEvent<SwapExecutedEventDTO>();
                            if (executed != null)
                            {
                                await HandleSwapExecuted(log, executed, NetworkName);
                                continue;
                            }

                            var failed = log.DecodeEvent<SwapFailedEventDTO>();
                            if (failed != null)
                            {
                                await HandleSwapFailed(log, failed, NetworkName);
                                continue;
                            }

                            var completed = log.DecodeEvent<SwapCompletedEventDTO>();
                            if (completed != null)
                            {
                                await HandleSwapCompleted(log, completed, NetworkName);
                                continue;
                            }

                            var refunded = log.DecodeEvent<SwapRefundedEventDTO>();
                            if (refunded != null)
                            {
                                await HandleSwapRefunded(log, refunded, NetworkName);
                                continue;
                            }

                            var refundClaimed = log.DecodeEvent<TokenRefundClaimedEventDTO>();
                            if (refundClaimed != null)
                            {
                                await HandleTokenRefundClaimed(log, refundClaimed, NetworkName);
                                continue;
                            }

                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "{Prefix} Error decoding polled log", SwapLogPrefix);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Prefix} Error polling logs from {FromBlock} to {ToBlock}", SwapLogPrefix, fromBlock, toBlock);
                }

                fromBlock = toBlock + 1;
                await Task.Delay(3000, cancellationToken);
            }

            lock (_blockLock)
            {
                _swapLastProcessedBlock = BigInteger.Max(_swapLastProcessedBlock, latestBlock);
                //_logger.LogInformation(
                // "{Prefix} Checking latest block: {Block}",
                // CommonLogPrefix,
                // latestBlock);
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

            lock (_blockLock)
            {
                _swapLastProcessedBlock =
                    BigInteger.Max(_swapLastProcessedBlock, log.BlockNumber.Value + 1);
            }
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

                var latestBlockNumber = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

                lock (_blockLock)
                {
                    _swapLastProcessedBlock = latestBlockNumber;
                    return latestBlockNumber;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "{Prefix} Error getting swap last processed block", SwapLogPrefix);
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

        private void InitializeClients()
        {
            _web3 = new Web3(GetCurrentRpcUrl());
        }

        private string GetCurrentRpcUrl()
        {
            return _rpcUrls[_currentRpcIndex];
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_isDisposed) return;

            _logger.LogInformation("{Prefix} Stopping polling service...", CommonLogPrefix);

            _isDisposed = true;
            await base.StopAsync(cancellationToken);
        }
    }
}
