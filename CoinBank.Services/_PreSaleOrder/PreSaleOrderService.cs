using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._BlockChain;
using CoinBank.Services._PreSale;
using CoinBank.Services._PreSaleOrder.DTOs.Results;
using CoinBank.Services._PreSaleOrder.DTOs.Updates;
using CoinBank.Services._PreSaleRelease;
using Microsoft.CodeAnalysis;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System.Xml.Linq;
using Utilities.Exceptions.Common;
using Utilities.Utilities;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._PreSaleOrder
{
    public class PreSaleOrderService(
        IPreSaleService _preSaleService,
        IPreSaleReleaseService _preSaleReleaseService,
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
            var presale = await _preSaleService
                .GetPreSaleDataByReferenceForInternalUsageAsync(update.PreSaleReference);

            var userOrders = await GetUserActiveOrders(presale.PreSaleReference, publicKey);

            ValidateUserOrderCount(userOrders);

            var userTotalAmount = userOrders.Sum(x => x.TokenAmount);
            await ValidateOrderAmount(presale, update.TokenAmount, userTotalAmount);

            await ValidateUserBalance(evmWalletAddress, presale.Price, update.TokenAmount);

            var newOrder = new PreSaleOrder
            {
                PreSaleOrderReference = Guid.NewGuid().ToString("N"),
                PreSaleReference = presale.PreSaleReference,
                Symbol = presale.Symbol,
                LogoUrl = presale.LogoUrl,
                Name = presale.Name,
                PaidToken = "RZUSD",
                RegisterHash = null,
                RegisterMoment = null,
                UserPublicKey = publicKey,
                WalletAddress = evmWalletAddress,
                State = PreSaleOrderState.NotRegistered,
                TokenPrice = presale.Price,
                TotalValue = presale.Price * update.TokenAmount,
                TokenAmount = update.TokenAmount,
                ReleaseSchedule = presale.ReleaseSchedule
            };

            //TODO : remove later
            newOrder.State = PreSaleOrderState.InProgress;
            await _preSaleOrderRepository.InsertOneAsync(newOrder);
            //TODO : remove later
            await _preSaleService.SyncPreSaleToStorageAsync(presale.PreSaleReference);

            return ConvertToResult(newOrder);
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

            if (string.IsNullOrWhiteSpace(publicKey)) throw new BadRequestException("Access denied!");

            if (publicKey == "guess")
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
                        TotalPrice = g.Sum(x => x.TotalValue),
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
                    TotalPrice = x.TotalValue,
                    OrderCount = 1
                })
                .ToListAsync();

            return list;
        }


        /// <summary>
        /// use for get one pre sale order detail with transactions(releases)
        /// </summary>
        /// <param name="update"></param>
        /// <param name="publicKey"></param>
        /// <param name="evmWalletAddress"></param>
        /// <returns></returns>
        /// <exception cref="NotFoundException"></exception>
        public async Task<PreSaleOrderDetailResult> GetOnePreSaleOrderDetailAsync(GetOnePreSaleOrderDetailUpdate update, string publicKey, string evmWalletAddress)
        {
            var query = _preSaleOrderRepository.AsQueryable().Where(x =>
                    x.PreSaleOrderReference == update.PreSaleOrderReference);

            if (string.IsNullOrWhiteSpace(publicKey) || publicKey == "guess")
            {
                query = query.Where(x => x.WalletAddress == evmWalletAddress);
            }
            else
            {
                query = query.Where(x => x.UserPublicKey == publicKey);
            }

            var order = await query.FirstOrDefaultAsync() ?? throw new NotFoundException("PreSale order not found!");

            var releases = await _preSaleReleaseService
                .GetReleasesOfPreSaleOrderByReferenceAsync(order.PreSaleOrderReference);

            decimal totalReleased = releases
                .Sum(x => x.ReleaseAmount);

            decimal remainForRelease = order.TokenAmount - totalReleased;

            return new PreSaleOrderDetailResult
            {
                CreatedMoment = order.CreatedMoment,
                ModifiedMoment = order.ModifiedMoment,
                PreSaleOrderReference = order.PreSaleOrderReference,
                Name = order.Name,
                Symbol = order.Symbol,
                LogoUrl = order.LogoUrl,
                WalletAddress = order.WalletAddress,
                TokenPrice = order.TokenPrice,
                TotalPrice = order.TotalValue,
                TokenAmount = order.TokenAmount,
                ReleaseSchedule = order.ReleaseSchedule,
                ReleaseTransactions = releases,
                RemainReleaseTokenAmount = remainForRelease,
                TotalReleasedTokenAmount = totalReleased,
                State = order.State
            };
        }


        ///// <summary>
        ///// this method is for preSale service 
        ///// </summary>
        ///// <param name="preSaleReference"></param>
        ///// <returns></returns>
        //public async Task MakeCompeletePreSaleOrderStateByPreSaleReferenceAsync(string preSaleReference)
        //{
        //    var now = DateTime.UtcNow;
        //    var builder = Builders<PreSaleOrder>.Filter;

        //    var filter = builder.And(
        //        builder.Eq(x => x.PreSaleReference, preSaleReference),
        //        builder.Eq(x => x.State, PreSaleOrderState.InProgress)
        //    );

        //    var update = Builders<PreSaleOrder>.Update
        //        .Set(x => x.State, PreSaleOrderState.Completed);

        //    await _preSaleOrderRepository.UpdateManyAsync(filter, update);
        //}


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
            return new PreSaleOrderResult
            {
                PreSaleOrderReference = preSaleOrder.PreSaleOrderReference,
                Name = preSaleOrder.Name,
                Symbol = preSaleOrder.Symbol,
                LogoUrl = preSaleOrder.LogoUrl,
                WalletAddress = preSaleOrder.WalletAddress,
                TokenPrice = preSaleOrder.TokenPrice,
                TotalValue = preSaleOrder.TotalValue,
                TokenAmount = preSaleOrder.TokenAmount,
                ReleaseSchedule = preSaleOrder.ReleaseSchedule,
                State = preSaleOrder.State,
                ModifiedMoment = preSaleOrder.ModifiedMoment,
                CreatedMoment = preSaleOrder.CreatedMoment
            };
        }

        private async Task ValidateUserBalance(string wallet, decimal price, decimal amount)
        {
            var balance = await _blockChainService
                .GetWalletAddressSingleTokenBalanceAsync(wallet, "RZUSD");

            var required = price * amount;

            if (balance < required)
                throw new BadRequestException("Insufficient RZUSD balance!");
        }

        private async Task<List<PreSaleOrder>> GetUserActiveOrders(string preSaleReference, string publicKey)
        {
            return await _preSaleOrderRepository.AsQueryable()
                .Where(x => x.PreSaleReference == preSaleReference &&
                            x.UserPublicKey == publicKey &&
                           (x.State == PreSaleOrderState.InProgress || x.State == PreSaleOrderState.Completed))
                .ToListAsync() ?? [];
        }

        private void ValidateUserOrderCount(List<PreSaleOrder> orders)
        {
            if (orders.Count >= 5)
                throw new BadRequestException("maximum order for each token is 5");
        }

        private async Task<decimal> GetTotalSoldAmount(string preSaleReference)
        {
            return await _preSaleOrderRepository.AsQueryable()
                .Where(x => x.PreSaleReference == preSaleReference &&
                       (x.State == PreSaleOrderState.InProgress || x.State == PreSaleOrderState.Completed))
                .SumAsync(x => (decimal?)x.TokenAmount) ?? 0;
        }

        private async Task ValidateOrderAmount(
         PreSale preSale,
         decimal requestAmount,
         decimal userTotalAmount)
        {
            // min/max per order
            if (requestAmount < preSale.MinPerOrder)
                throw new BadRequestException($"Minimum amount is {preSale.MinPerOrder} {preSale.Symbol}");

            if (requestAmount > preSale.MaxPerOrder)
                throw new BadRequestException($"Maximum amount is {preSale.MaxPerOrder} {preSale.Symbol}");

            // user remaining quota
            var userRemain = preSale.MaxPerOrder - userTotalAmount;
            if (requestAmount > userRemain)
                throw new BadRequestException($"Your remaining quota is {userRemain} {preSale.Symbol}");


            var totalSold = await GetTotalSoldAmount(preSale.PreSaleReference);

            // total supply check
            var remainingSupply = preSale.TotalSupply - totalSold;

            if (remainingSupply <= 0)
                throw new BadRequestException("PreSale is sold out");

            if (requestAmount > remainingSupply)
                throw new BadRequestException($"Remaining total supply is {remainingSupply} {preSale.Symbol}");
        }



    }
}
