using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._BlockChain;
using CoinBank.Services._PreSaleOrder.DTOs.Results;
using CoinBank.Services._PreSaleOrder.DTOs.Updates;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Utilities.Exceptions.Common;
using Utilities.Utilities;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._PreSaleOrder
{
    public class PreSaleOrderService(
        IPreSaleRepository _preSaleRepository,
        IPreSaleOrderRepository _preSaleOrderRepository,
        IBlockChainService _blockChainService) : IPreSaleOrderService, IScopedDependency
    {

        /// <summary>
        /// use for create pre sale order 
        /// </summary>
        /// <param name="update"></param>
        /// <param name="publicKey"></param>
        /// <param name="evmWalletAddress"></param>
        /// <returns></returns>
        public async Task<PreSaleOrderResult> CreatePreSaleOrderAsync(CreatePreSaleOrderUpdate update, string publicKey, string evmWalletAddress)
        {
            var presale = await GetPreSaleDataBySymbolAsync(update.Symbol);

            var rzusdbalance = await _blockChainService.GetWalletAddressSingleTokenBalanceAsync(evmWalletAddress, "RZUSD");
            decimal neededRzusdForPay = presale.Price * update.TokenAmount;
            if (rzusdbalance < neededRzusdForPay)
                throw new BadRequestException("Insufficient RZUSD balance!");

            var newPreSaleOrder = new PreSaleOrder
            {
                PreSaleReference = presale.PreSaleReference,
                Symbol = presale.Symbol.ToUpper(),
                LogoUrl = presale.LogoUrl,
                Name = presale.Name,
                PreSaleOrderReference = Guid.NewGuid().ToString("N"),
                PaidToken = null,
                RegisterHash = null,
                RegisterMoment = null,
                UserPublicKey = publicKey,
                WalletAddress = evmWalletAddress,
                TokenPrice = presale.Price,
                State = PreSaleOrderState.NotRegistered,
                TotalPrice = presale.Price * update.TokenAmount,
                TokenAmount = update.TokenAmount,
                ReleaseSchedule = presale.ReleaseSchedule
            };
            await _preSaleOrderRepository.InsertOneAsync(newPreSaleOrder);
            return ConvertToResult(newPreSaleOrder);
        }


        /// <summary>
        /// use for get user preSale history
        /// </summary>
        /// <param name="update"></param>
        /// <param name="publicKey"></param>
        /// <param name="evmWalletAddress"></param>
        /// <returns></returns>
        public async Task<PreSaleOrderListResult> GetPreSaleOrderHistoryAsync(GetPreSaleOrderHistoryUpdate update, string publicKey, string evmWalletAddress)
        {

            var query = _preSaleOrderRepository.AsQueryable();

            if (update.Symbol != null && update.Symbol.HasValue())
            {
                query = query.Where(q => q.Symbol == update.Symbol.ToUpper());
            }

            if (string.IsNullOrWhiteSpace(publicKey) || publicKey == "guess")
            {
                query = query.Where(x =>
                    x.WalletAddress == evmWalletAddress &&
                    x.State != PreSaleOrderState.NotRegistered);
            }
            else
            {
                query = query.Where(x =>
                    (x.UserPublicKey == publicKey) &&
                    x.State != PreSaleOrderState.NotRegistered);
            }

            var totalCount = await query.CountAsync();

            var page = update.Pagination?.Page ?? 1;
            var size = update.Pagination?.Size ?? 25;

            var data = await query
                .OrderByDescending(x => x.RegisterMoment)
                .Skip((page - 1) * size)
                .Take(size)
                .ToListAsync();

            var result = new PreSaleOrderListResult
            {
                TotalCount = totalCount,
                PageCount = (int)Math.Ceiling((double)totalCount / size),
                Data = data.Select(ConvertToResult).ToList()
            };

            return result;
        }


        /// <summary>
        /// use for get pre sale order wallet stats
        /// </summary>
        /// <param name="update"></param>
        /// <param name="publicKey"></param>
        /// <param name="evmWalletAddress"></param>
        /// <returns></returns>
        public async Task<List<PreSaleOrderWalletStatsResult>> GetWalletStatsAsync(GetPreSaleOrderWalletStatsUpdate update, string publicKey, string evmWalletAddress)
        {
            var query = _preSaleOrderRepository.AsQueryable();

            if (string.IsNullOrWhiteSpace(publicKey) || publicKey == "guess")
            {
                query = query.Where(x =>
                    x.WalletAddress == evmWalletAddress &&
                    x.State != PreSaleOrderState.NotRegistered);
            }
            else
            {
                query = query.Where(x =>
                    x.UserPublicKey == publicKey &&
                    x.State != PreSaleOrderState.NotRegistered);
            }

            if (update.ShouldGrouped)
            {
                var grouped = await query
                    .GroupBy(x => x.Symbol)
                    .Select(g => new PreSaleOrderWalletStatsResult
                    {
                        Symbol = g.Key,
                        Name = g.First().Name,
                        LogoUrl = g.First().LogoUrl,
                        TokenPrice = g.First().TokenPrice,
                        ReleaseSchedule = g.First().ReleaseSchedule,

                        TokenAmount = g.Sum(x => x.TokenAmount),
                        TotalPrice = g.Sum(x => x.TotalPrice),
                        OrderCount = g.Count()
                    })
                    .ToListAsync();

                return grouped;
            }

            var list = await query
                .Select(x => new PreSaleOrderWalletStatsResult
                {
                    Symbol = x.Symbol,
                    Name = x.Name,
                    LogoUrl = x.LogoUrl,
                    TokenPrice = x.TokenPrice,
                    ReleaseSchedule = x.ReleaseSchedule,

                    TokenAmount = x.TokenAmount,
                    TotalPrice = x.TotalPrice,
                    OrderCount = 1
                })
                .ToListAsync();

            return list;
        }


        /// <summary>
        /// this method is for preSale service 
        /// </summary>
        /// <param name="preSaleReference"></param>
        /// <returns></returns>
        public async Task MakeCompeletePreSaleOrderStateByPreSaleReferenceAsync(string preSaleReference)
        {
            var now = DateTime.UtcNow;
            var builder = Builders<PreSaleOrder>.Filter;

            var filter = builder.And(
                builder.Eq(x => x.PreSaleReference, preSaleReference),
                builder.Eq(x => x.State, PreSaleOrderState.InProgress)
            );

            var update = Builders<PreSaleOrder>.Update
                .Set(x => x.State, PreSaleOrderState.Completed);

            await _preSaleOrderRepository.UpdateManyAsync(filter, update);
        }


        /// <summary>
        /// use for remove old NotRegistered PreSale Orders
        /// </summary>
        /// <returns></returns>
        public async Task RemoveNotRegisteredPreSaleOrderAsync()
        {
            var oneWeekAgo = DateTime.UtcNow.AddDays(-30);

            var query = _preSaleOrderRepository.AsQueryable();

            var ordersToDelete = await query
                .Where(x =>
                    x.RegisterHash == null &&
                    x.State == PreSaleOrderState.NotRegistered &&
                    x.CreatedMoment <= oneWeekAgo)
                .ToListAsync();

            if (ordersToDelete == null || ordersToDelete.Count == 0)
                return;

            var ids = ordersToDelete.Select(x => x.Id).ToList();

            await _preSaleOrderRepository.DeleteManyAsync(x => ids.Contains(x.Id));
        }

        /// <summary>
        /// convertor method
        /// </summary>
        /// <param name="preSaleOrder"></param>
        /// <returns></returns>
        private PreSaleOrderResult ConvertToResult(PreSaleOrder preSaleOrder)
        {
            decimal availableAmount = 0;

            if (preSaleOrder.ReleaseSchedule != null && preSaleOrder.ReleaseSchedule.Any())
            {
                var now = DateTime.UtcNow;

                foreach (var step in preSaleOrder.ReleaseSchedule)
                {
                    if (step.ReleaseDate <= now)
                    {
                        availableAmount += preSaleOrder.TokenAmount * step.Percentage / 100;
                    }
                }
            }

            return new PreSaleOrderResult
            {
                PreSaleOrderReference = preSaleOrder.PreSaleOrderReference,
                Name = preSaleOrder.Name,
                Symbol = preSaleOrder.Symbol,
                LogoUrl = preSaleOrder.LogoUrl,
                WalletAddress = preSaleOrder.WalletAddress,
                TokenPrice = preSaleOrder.TokenPrice,
                TotalPrice = preSaleOrder.TotalPrice,
                TokenAmount = preSaleOrder.TokenAmount,
                AvailableForWithdrawalAmount = availableAmount,
                ReleaseSchedule = preSaleOrder.ReleaseSchedule
            };
        }


        /// <summary>
        /// for get preSale data by reference
        /// </summary>
        /// <param name="symbol"></param>
        /// <returns></returns>
        /// <exception cref="NotFoundException"></exception>
        private async Task<PreSale> GetPreSaleDataBySymbolAsync(string symbol)
        {
            return await _preSaleRepository.AsQueryable().FirstOrDefaultAsync(q => q.Symbol.ToLower() == symbol.ToLower()) ??
                throw new NotFoundException("PreSale token not found!");
        }




    }
}
