using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._PreSaleOrder;
using CoinBank.Services._Stake;
using CoinBank.Services._Swap;
using CoinBank.Services._Transaction._Hub;
using CoinBank.Services._Transaction.DTOs.Updates;
using CoinBank.Services._Withdrawal;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Linq;
using System.Numerics;
using System.Text.Json;
using Utilities.Exceptions.Common;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Transaction
{
    public class TransactionLogService(
        ITransactionLogRepository _transactionLogRepository,
        ILogger<TransactionLogService> _logger,
        IPreSaleOrderService _preSaleOrderService,
        ISwapService _swapService,
        IWithdrawalService _withdrawalService,
        IStakeService _stakeService,
        AvailableTokensSettings _availableTokenData,
        IHubContext<WalletNotifyHub> _hubContext) : ITransactionLogService, IScopedDependency
    {

        #region PreSale
        public async Task CreatePreSaleOrderCreateLogAsync(PreSaleOrderCreateLog input)
        {
            try
            {
                var existsLog = await _transactionLogRepository.AsQueryable()
                    .Where(q =>
                        q.Hash.ToLower() == input.Hash.ToLower() &&
                        q.Reference.ToLower() == input.OrderId.ToLower() &&
                        q.EventType == BlockchainEventType.PreSaleOrderCreate)
                    .FirstOrDefaultAsync();

                if (existsLog != null)
                {
                    _logger.LogWarning(
                        "Duplicate PreSaleOrderCreate log detected for OrderId {OrderId}. Skipping insertion. Hash: {Hash}",
                        input.OrderId, input.Hash);

                    return;
                }

                var newLog = new TransactionLog
                {
                    Hash = input.Hash,
                    Wallet = input.Buyer,
                    BlockNumber = (decimal)input.BlockNumber,
                    EventType = BlockchainEventType.PreSaleOrderCreate,
                    Status = TransactionStatus.Confirmed,
                    Network = "BEP20",

                    Reference = input.OrderId,
                    TokenAddress = input.Address,

                    Data = SerializeData(input)
                };

                await _transactionLogRepository.InsertOneAsync(newLog);

                await _preSaleOrderService.ActivatePreSaleOrderForInternalUsageAsync(newLog.Reference, newLog.Hash);

                try
                {
                    var shortOrderId = input.OrderId.Length > 10 ? input.OrderId[..10] : input.OrderId;

                    await _hubContext.Clients.Group(input.Buyer)
                        .SendAsync("PreSaleMessage", $"Your order has been created successfully.");
                }
                catch (Exception)
                {
                    _logger.LogError("Failed to send PreSaleOrderCreate notification for OrderId {OrderId}", input.OrderId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating PreSaleOrderCreate transaction log.");
            }

        }

        public async Task CreatePreSaleReleaseClaimedLogAsync(PreSaleReleaseClaimedLog input)
        {
            try
            {
                var existsLog = await _transactionLogRepository.AsQueryable()
                    .Where(q =>
                        q.Hash.ToLower() == input.Hash.ToLower() &&
                        q.Reference.ToLower() == input.OrderId.ToLower() &&
                        q.EventType == BlockchainEventType.PreSaleReleaseClaimed)
                    .FirstOrDefaultAsync();

                if (existsLog != null)
                {
                    _logger.LogWarning(
                        "Duplicate PreSaleReleaseClaimed log detected for OrderId {OrderId}. Skipping insertion. Hash: {Hash}",
                        input.OrderId, input.Hash);
                    return;
                }

                var newLog = new TransactionLog
                {
                    Hash = input.Hash,
                    Wallet = input.Buyer,
                    BlockNumber = (decimal)input.BlockNumber,
                    EventType = BlockchainEventType.PreSaleReleaseClaimed,
                    Status = TransactionStatus.Confirmed,
                    Network = "BEP20",

                    Reference = input.OrderId,
                    TokenAddress = input.Address,

                    Data = SerializeData(input)
                };

                await _transactionLogRepository.InsertOneAsync(newLog);

                //await _preSaleOrderService.UpdatePreSaleReleaseStepForAddTransactionAsync(input.OrderId, input.Hash, input.AmountClaimed);

                try
                {
                    var shortOrderId = input.OrderId.Length > 10 ? input.OrderId[..10] : input.OrderId;

                    await _hubContext.Clients.Group(input.Buyer)
                        .SendAsync("PreSaleMessage", $"Your claim for order {shortOrderId}... was successful.");
                }
                catch (Exception)
                {
                    _logger.LogError("Failed to send PreSaleReleaseClaimed notification for OrderId {OrderId}", input.OrderId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating PreSaleReleaseClaimed transaction log.");
            }
        }

        public async Task<BigInteger> GetPreSaleOrderLastCheckedBlockNumberAsync()
        {
            var lastBlock = await _transactionLogRepository
             .AsQueryable()
             .Where(h => h.EventType == BlockchainEventType.PreSaleOrderCreate)
             .OrderByDescending(b => b)
             .FirstOrDefaultAsync();
            if (lastBlock == null)
            {
                return BigInteger.Zero;
            }
            return new BigInteger(lastBlock.BlockNumber);
        }

        #endregion



        #region Swap

        public async Task CreateSwapInitiatedLogAsync(SwapInitiatedLog input)
        {
            try
            {
                var existsLog = await _transactionLogRepository.AsQueryable()
                    .Where(q =>
                        q.Hash.ToLower() == input.Hash.ToLower() &&
                        q.Reference.ToLower() == input.SwapId.ToLower() &&
                        q.EventType == BlockchainEventType.SwapInitiated)
                    .FirstOrDefaultAsync();

                if (existsLog != null)
                {
                    _logger.LogWarning(
                        "Duplicate SwapInitiated log detected for SwapId {SwapId}. Skipping insertion. Hash: {Hash}",
                        input.SwapId, input.Hash);
                    return;
                }



                var newLog = new TransactionLog
                {
                    Hash = input.Hash,
                    Wallet = input.Buyer,
                    BlockNumber = (decimal)input.BlockNumber,
                    EventType = BlockchainEventType.SwapInitiated,
                    Status = TransactionStatus.Confirmed,
                    Network = input.Network,

                    Reference = input.SwapId,
                    TokenAddress = input.SourceTokenAddress,

                    Data = SerializeData(input)
                };

                await _transactionLogRepository.InsertOneAsync(newLog);


                await _swapService.AddTransactionToSwapAsync(new _Swap.DTOs.Updates.AddTransactionToSwapUpdate
                {
                    SwapReference = input.SwapId,
                    Amount = input.SourceTokenAmount,
                    Hash = input.Hash,
                    Network = input.Network,
                    TokenAddress = input.SourceTokenAddress,
                    Type = SwapTransactionType.Init
                });

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating SwapInitiated transaction log.");
            }
        }

        public async Task CreateSwapExecutedLogAsync(SwapExecutedLog input)
        {
            try
            {
                var existsLog = await _transactionLogRepository.AsQueryable()
                    .Where(q =>
                        q.Hash.ToLower() == input.Hash.ToLower() &&
                        q.Reference.ToLower() == input.SwapId.ToLower() &&
                        q.EventType == BlockchainEventType.SwapExecuted)
                    .FirstOrDefaultAsync();

                if (existsLog != null)
                {
                    _logger.LogWarning(
                        "Duplicate SwapExecuted log detected for SwapId {SwapId}. Skipping insertion. Hash: {Hash}",
                        input.SwapId, input.Hash);
                    return;
                }

                var newLog = new TransactionLog
                {
                    Hash = input.Hash,
                    Wallet = input.DestinationWallet,
                    BlockNumber = (decimal)input.BlockNumber,
                    EventType = BlockchainEventType.SwapExecuted,
                    Status = TransactionStatus.Confirmed,
                    Network = input.Network,

                    Reference = input.SwapId,
                    TokenAddress = input.DestinationTokenAddress,

                    Data = SerializeData(input)
                };

                await _transactionLogRepository.InsertOneAsync(newLog);


                await _swapService.AddTransactionToSwapAsync(new _Swap.DTOs.Updates.AddTransactionToSwapUpdate
                {
                    SwapReference = input.SwapId,
                    Amount = input.DestinationTokenAmount,
                    Hash = input.Hash,
                    Network = input.Network,
                    TokenAddress = input.DestinationTokenAddress,
                    Type = SwapTransactionType.Execute
                });

              
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating SwapExecuted transaction log.");
            }
        }

        public async Task CreateSwapCompletedLogAsync(SwapCompletedLog input)
        {
            try
            {
                var existsLog = await _transactionLogRepository.AsQueryable()
                    .Where(q =>
                        q.Hash.ToLower() == input.Hash.ToLower() &&
                        q.Reference.ToLower() == input.SwapId.ToLower() &&
                        q.EventType == BlockchainEventType.SwapCompleted)
                    .FirstOrDefaultAsync();

                if (existsLog != null)
                {
                    _logger.LogWarning(
                        "Duplicate SwapCompleted log detected for SwapId {SwapId}. Skipping insertion. Hash: {Hash}",
                        input.SwapId, input.Hash);
                    return;
                }

                var newLog = new TransactionLog
                {
                    Hash = input.Hash,
                    Wallet = null,
                    BlockNumber = (decimal)input.BlockNumber,
                    EventType = BlockchainEventType.SwapCompleted,
                    Status = TransactionStatus.Confirmed,
                    Network = input.Network,

                    Reference = input.SwapId,
                    TokenAddress = input.Address,

                    Data = SerializeData(input)
                };

                await _transactionLogRepository.InsertOneAsync(newLog);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating SwapCompleted transaction log.");
            }
        }
        
        public async Task CreateSwapFailedLogAsync(SwapFailedLog input)
        {
            try
            {
                var existsLog = await _transactionLogRepository.AsQueryable()
                    .Where(q =>
                        q.Hash.ToLower() == input.Hash.ToLower() &&
                        q.Reference.ToLower() == input.SwapId.ToLower() &&
                        q.EventType == BlockchainEventType.SwapFailed)
                    .FirstOrDefaultAsync();

                if (existsLog != null)
                {
                    _logger.LogWarning(
                        "Duplicate SwapFailed log detected for SwapId {SwapId}. Skipping insertion. Hash: {Hash}",
                        input.SwapId, input.Hash);
                    return;
                }

                var newLog = new TransactionLog
                {
                    Hash = input.Hash,
                    Wallet = input.DestinationWallet,
                    BlockNumber = (decimal)input.BlockNumber,
                    EventType = BlockchainEventType.SwapFailed,
                    Status = TransactionStatus.Failed,
                    Network = input.Network,
                    Reference = input.SwapId,
                    TokenAddress = input.DestinationTokenAddress,

                    Data = SerializeData(input)
                };

                await _transactionLogRepository.InsertOneAsync(newLog);

                await _swapService.AddTransactionToSwapAsync(new _Swap.DTOs.Updates.AddTransactionToSwapUpdate
                {
                    SwapReference = input.SwapId,
                    Amount = input.DestinationTokenAmount,
                    Hash = input.Hash,
                    Network = input.Network,
                    TokenAddress = input.DestinationTokenAddress,
                    Type = SwapTransactionType.Failed
                });


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating SwapFailed transaction log.");
            }
        }

        public async Task CreateSwapRefundedLogAsync(SwapRefundedLog input)
        {
            try
            {
                var existsLog = await _transactionLogRepository.AsQueryable()
                    .Where(q =>
                        q.Hash.ToLower() == input.Hash.ToLower() &&
                        q.Reference.ToLower() == input.SwapId.ToLower() &&
                        q.EventType == BlockchainEventType.SwapRefunded)
                    .FirstOrDefaultAsync();

                if (existsLog != null)
                {
                    _logger.LogWarning(
                        "Duplicate SwapRefunded log detected for SwapId {SwapId}. Skipping insertion. Hash: {Hash}",
                        input.SwapId, input.Hash);
                    return;
                }

                var newLog = new TransactionLog
                {
                    Hash = input.Hash,
                    Wallet = input.User,
                    BlockNumber = (decimal)input.BlockNumber,
                    EventType = BlockchainEventType.SwapRefunded,
                    Status = TransactionStatus.Confirmed,
                    Network = input.Network,

                    Reference = input.SwapId,
                    TokenAddress = input.Token,

                    Data = SerializeData(input)
                };

                await _transactionLogRepository.InsertOneAsync(newLog);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating SwapRefunded transaction log.");
            }
        }

        public async Task<BigInteger> GetSwapLastCheckedBlockNumberAsync(string network = "BEP20")
        {

            var lastBlock = await _transactionLogRepository
                .AsQueryable()
                .Where(h =>
                    h.EventType == BlockchainEventType.SwapExecuted && h.Network == network)
                //||
                //    h.EventType == BlockchainEventType.SwapExecuted ||
                //    h.EventType == BlockchainEventType.SwapFailed)
                .OrderByDescending(b => b.BlockNumber)
                .FirstOrDefaultAsync();

            if (lastBlock == null)
            {
                return BigInteger.Zero;
            }

            return new BigInteger(lastBlock.BlockNumber);
        }

        #endregion




        #region Stake

        public async Task CreateDepositCreatedLogAsync(DepositCreatedLog input)
        {
            try
            {
                var existsLog = await _transactionLogRepository.AsQueryable()
                    .Where(q =>
                        q.Hash.ToLower() == input.Hash.ToLower() &&
                        q.Reference.ToLower() == input.DepositId.ToLower() &&
                        q.EventType == BlockchainEventType.DepositCreated)
                    .FirstOrDefaultAsync();

                if (existsLog != null)
                {
                    _logger.LogWarning(
                        "Duplicate DepositCreated log detected for DepositId {DepositId}. Skipping insertion. Hash: {Hash}",
                        input.DepositId, input.Hash);
                    return;
                }

                var newLog = new TransactionLog
                {
                    Hash = input.Hash,
                    Wallet = input.Depositor,
                    BlockNumber = (decimal)input.BlockNumber,
                    EventType = BlockchainEventType.DepositCreated,
                    Status = TransactionStatus.Confirmed,
                    Network = input.Network,
                    Reference = input.DepositId,
                    TokenAddress = input.Token,
                    Data = SerializeData(input)
                };

                await _transactionLogRepository.InsertOneAsync(newLog);
                await _stakeService.ActivateStakeAsync(input.DepositId, input.Hash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating DepositCreated transaction log.");
            }
        }

        //public async Task CreateEarlyWithdrawnLogAsync(EarlyWithdrawnLog input)
        //{
        //    try
        //    {
        //        var existsLog = await _transactionLogRepository.AsQueryable()
        //            .Where(q =>
        //                q.Hash.ToLower() == input.Hash.ToLower() &&
        //                q.Reference.ToLower() == input.DepositId.ToLower() &&
        //                q.EventType == BlockchainEventType.EarlyWithdrawn)
        //            .FirstOrDefaultAsync();

        //        if (existsLog != null)
        //        {
        //            _logger.LogWarning(
        //                "Duplicate EarlyWithdrawn log detected for DepositId {DepositId}. Skipping insertion. Hash: {Hash}",
        //                input.DepositId, input.Hash);
        //            return;
        //        }

        //        var newLog = new TransactionLog
        //        {
        //            Hash = input.Hash,
        //            Wallet = input.Depositor,
        //            BlockNumber = (decimal)input.BlockNumber,
        //            EventType = BlockchainEventType.EarlyWithdrawn,
        //            Status = TransactionStatus.Confirmed,
        //            Network = input.Network,

        //            Reference = input.DepositId,
        //            Data = SerializeData(input)
        //        };

        //        await _transactionLogRepository.InsertOneAsync(newLog);
        //        await _withdrawalService.CreateEarlyWithdrawnByEventAsync(input.DepositId, input.Hash, input.WithdrawAmount, input.ProfitAmount, input.ClaimedProfitAmount);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error while creating EarlyWithdrawn transaction log.");
        //    }
        //}

        public async Task CreateProfitWithdrawnLogAsync(ProfitWithdrawnLog input)
        {
            try
            {
                var existsLog = await _transactionLogRepository.AsQueryable()
                    .Where(q =>
                        q.Hash.ToLower() == input.Hash.ToLower() &&
                        q.Reference.ToLower() == input.DepositId.ToLower() &&
                        q.EventType == BlockchainEventType.ProfitWithdrawn)
                    .FirstOrDefaultAsync();

                if (existsLog != null)
                {
                    _logger.LogWarning(
                        "Duplicate ProfitWithdrawn log detected for DepositId {DepositId}. Skipping insertion. Hash: {Hash}",
                        input.DepositId, input.Hash);
                    return;
                }

                var newLog = new TransactionLog
                {
                    Hash = input.Hash,
                    Wallet = input.Depositor,
                    BlockNumber = (decimal)input.BlockNumber,
                    EventType = BlockchainEventType.ProfitWithdrawn,
                    Status = TransactionStatus.Confirmed,
                    Network = input.Network,

                    Reference = input.DepositId,
                    TokenAddress = input.Token,

                    Data = SerializeData(input)
                };

                await _transactionLogRepository.InsertOneAsync(newLog);
                await _withdrawalService.CreateProfitWithdrawaByEventAsycn(input.DepositId, input.Profit, input.Hash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating ProfitWithdrawn transaction log.");
            }
        }

        public async Task CreateWithdrawnLogAsync(WithdrawnLog input)
        {
            try
            {
                var existsLog = await _transactionLogRepository.AsQueryable()
                    .Where(q =>
                        q.Hash.ToLower() == input.Hash.ToLower() &&
                        q.Reference.ToLower() == input.DepositId.ToLower() &&
                        q.EventType == BlockchainEventType.WithdrawnAll)
                    .FirstOrDefaultAsync();

                if (existsLog != null)
                {
                    _logger.LogWarning(
                        "Duplicate Withdrawn log detected for DepositId {DepositId}. Skipping insertion. Hash: {Hash}",
                        input.DepositId, input.Hash);
                    return;
                }

                var newLog = new TransactionLog
                {
                    Hash = input.Hash,
                    Wallet = input.Depositor,
                    BlockNumber = (decimal)input.BlockNumber,
                    EventType = BlockchainEventType.WithdrawnAll,
                    Status = TransactionStatus.Confirmed,
                    Network = input.Network,

                    Reference = input.DepositId,

                    Data = SerializeData(input)
                };

                await _transactionLogRepository.InsertOneAsync(newLog);
                await _withdrawalService.CreateWithdrawnAllByEventAsync(input.DepositId, input.Hash, input.Principal, input.Profit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating Withdrawn transaction log.");
            }
        }

        public async Task<BigInteger> GetDepositLastCheckedBlockNumberAsync(string network = "BEP20")
        {
            var lastBlock = await _transactionLogRepository
                .AsQueryable()
                .Where(h =>
                    h.Network == network &&
                    (
                        h.EventType == BlockchainEventType.DepositCreated
                    ))
                .OrderByDescending(b => b.BlockNumber)
                .FirstOrDefaultAsync();

            if (lastBlock == null)
            {
                return BigInteger.Zero;
            }

            return new BigInteger(lastBlock.BlockNumber);
        }

        #endregion




        /// <summary>
        /// use to get last checked block number for transaction confirmation
        /// </summary>
        /// <returns></returns>
        public async Task<BigInteger> GetLastCheckedBlockNumberAsync()
        {
            var lastBlock = await _transactionLogRepository
             .AsQueryable()
             .Where(h => h.EventType == BlockchainEventType.PreSaleOrderCreate)
             .OrderByDescending(b => b)
             .FirstOrDefaultAsync();
            if (lastBlock == null)
            {
                return BigInteger.Zero;
            }
            return new BigInteger(lastBlock.BlockNumber);
        }




        private string SerializeData<T>(T input)
        {
            object data = input switch
            {
                PreSaleOrderCreateLog x => new PreSaleOrderCreateData
                {
                    SaleId = x.SaleId,
                    OrderId = x.OrderId,
                    Buyer = x.Buyer,
                    AmountPurchased = x.AmountPurchased,
                    AmountPaid = x.AmountPaid
                },

                PreSaleReleaseClaimedLog x => new PreSaleReleaseClaimedData
                {
                    SaleId = x.SaleId,
                    OrderId = x.OrderId,
                    Buyer = x.Buyer,
                    AmountClaimed = x.AmountClaimed
                },

                SwapInitiatedLog x => new SwapInitiatedLogData
                {
                    SwapId = x.SwapId,
                    SourceTokenAddress = x.SourceTokenAddress,
                    DestinationTokenAddress = x.DestinationTokenAddress,
                    DesEid = x.DesEid.ToString(),
                    SourceTokenAmount = x.SourceTokenAmount.ToString(),
                    DestinationTokenAmount = x.DestinationTokenAmount.ToString(),
                    DestinationWallet = x.DestinationWallet,
                    Fee = x.Fee
                },

                SwapExecutedLog x => new SwapExecutedLogData
                {
                    SwapId = x.SwapId,
                    DestinationTokenAddress = x.DestinationTokenAddress,
                    DestinationTokenAmount = x.DestinationTokenAmount.ToString(),
                    DestinationWallet = x.DestinationWallet
                },

                SwapFailedLog x => new SwapFailedLogData
                {
                    SwapId = x.SwapId,
                    DestinationTokenAddress = x.DestinationTokenAddress,
                    DestinationTokenAmount = x.DestinationTokenAmount.ToString(),
                    DestinationWallet = x.DestinationWallet
                },

                SwapCompletedLog x => new SwapCompletedLogData
                {
                    SwapId = x.SwapId,
                },

                SwapRefundedLog x => new SwapRefundedLogData
                {
                    SwapId = x.SwapId,
                    Token = x.Token,
                    Amount = x.Amount.ToString(),
                    User = x.User
                },
                DepositCreatedLog x => new DepositCreatedData
                {
                    Depositor = x.Depositor.ToString(),
                    UnlocksAt = x.UnlocksAt.ToString(),
                    Profit = x.Profit.ToString(),
                    Principal = x.Principal.ToString(),
                    LockDuration = x.LockDuration.ToString()
                },

                ProfitWithdrawnLog x => new ProfitWithdrawnData
                {
                    Depositor = x.Depositor,
                    Profit = x.Profit.ToString()
                },

                WithdrawnLog x => new WithdrawnData
                {
                    Depositor = x.Depositor,
                    Principal = x.Principal.ToString(),
                    Profit = x.Profit.ToString(),
                    TotalPayout = x.TotalPayout.ToString()
                },

                _ => throw new NotSupportedException($"No serializer defined for type {typeof(T).Name}")
            };

            return JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

        //var data = DeserializeData<PreSaleOrderCreateData>(log.Data);
        private T? DeserializeData<T>(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return default;

            return JsonSerializer.Deserialize<T>(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }

       

    }
}
