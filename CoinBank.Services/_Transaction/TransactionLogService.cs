using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._PreSaleOrder;
using CoinBank.Services._Transaction._Hub;
using CoinBank.Services._Transaction.DTOs.Updates;
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
        AvailableTokensSettings _availableTokenData,
        IHubContext<WalletNotifyHub> _hubContext) : ITransactionLogService, IScopedDependency
    {

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

        private AvailableTokenData ValidateToken(string tokenName)
        {

            if (tokenName == null)
                throw new BadRequestException($"Unsupported token name! {tokenName}");

            var tokenData = _availableTokenData.FirstOrDefault(q => q.Name.Equals(tokenName, StringComparison.OrdinalIgnoreCase))
                ?? throw new BadRequestException($"Unsupported token name! {tokenName}");
            return tokenData;
        }

    }
}
