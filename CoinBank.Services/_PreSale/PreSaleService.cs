using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._BlockChain;
using CoinBank.Services._BlockChain.DTOs.Updates;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._Common.Services;
using CoinBank.Services._PreSale.DTOs.Results;
using CoinBank.Services._PreSale.DTOs.Storages;
using CoinBank.Services._PreSale.DTOs.Updates;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Utilities.DTOs;
using Utilities.Exceptions.Common;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._PreSale
{
    public class PreSaleService(IPreSaleRepository _preSaleRepository,
        IPreSaleOrderRepository _preSaleOrderRepository,
        IBlockChainService _blockChainService,
        AvailableTokensSettings _availableTokenData,
        PreSaleStorage _preSaleStorage) : IPreSaleService, IScopedDependency

    {

        public async Task<PreSaleResult> CreatePreSaleTokenAsync(CreatePreSaleTokenUpdate update)
        {
            ValidateCreateRequest(update);
            var symbol = update.Symbol.Trim().ToUpper();

            var existing = await _preSaleRepository.AsQueryable().FirstOrDefaultAsync(q => q.Symbol == symbol && q.State == PreSaleState.Active);

            if (existing != null)
                throw new BadRequestException("Active PreSale with this symbol already exists.");

            var newPreSale = new PreSale
            {
                PreSaleReference = IdGenerartor.GenerateBytes32HexId(),
                Name = update.Name,
                Symbol = symbol,
                LogoUrl = update.LogoUrl,
                Description = update.Description,
                TotalSupply = update.TotalSupply,
                MaxPerOrder = update.MaxPerOrder,
                MinPerOrder = update.MinPerOrder,
                Price = update.Price,
                StartSellingAt = update.StartSellingAt,
                EndSellingAt = update.EndSellingAt,
                ReleaseSchedule = update.ReleaseSchedule,
                State = PreSaleState.Pending,
                RegisterMoment = null
            };


            var registerHash = await _blockChainService.PreSaleConfigureAsync(newPreSale);

            if (registerHash == null || registerHash.IsNullOrEmpty()) throw new BadRequestException("Error in submit on blockChain!");

            newPreSale.RegisterHash = registerHash;
            newPreSale.RegisterMoment = DateTime.UtcNow;
            newPreSale.State = PreSaleState.Active;

            await _preSaleRepository.InsertOneAsync(newPreSale);

            await SyncPreSaleToStorageAsync(newPreSale);
            return MapToResult(newPreSale);
        }


        public async Task<List<PreSaleUserStatResult>> GetUserPreSaleStatsAsync(string publicKey, string evmWalletAddress)
        {
            var query = _preSaleOrderRepository.AsQueryable().Where(x => x.State == PreSaleOrderState.InProgress || x.State == PreSaleOrderState.Completed);

            if (string.IsNullOrWhiteSpace(publicKey) || publicKey == "guess")
            {
                query = query.Where(x => x.WalletAddress == evmWalletAddress);
            }
            else
            {
                query = query.Where(x => x.UserPublicKey == publicKey);
            }

            var userStats = await query
                .GroupBy(x => x.PreSaleReference)
                .Select(g => new PreSaleUserStatResult
                {
                    PreSaleReference = g.Key,
                    OrderCount = g.Count(),
                    TotalBought = g.Sum(x => x.ReceivingTokenAmount)
                })
                .ToListAsync();

            return userStats;
        }


        public async Task<PreSaleResult> GetOnePreSaleTokenAsync(GetOnePreSaleTokenUpdate update)
        {
            var preSale = await _preSaleRepository.AsQueryable().FirstOrDefaultAsync(q => q.PreSaleReference == update.PreSaleReference) ??
                throw new NotFoundException("PreSale not found.");

            return MapToResult(preSale);
        }

        public async Task<PreSaleListResult> GetAllPreSaleTokensForUserAsync(Pagination pagination, string publicKey, string evmWalletAddress)
        {
            var query = _preSaleRepository.AsQueryable();

            var totalCount = await query.CountAsync();
            var pageCount = (int)Math.Ceiling((double)totalCount / pagination.Size);

            var skip = (pagination.Page - 1) * pagination.Size;

            var data = await query
                .OrderByDescending(q => q.CreatedMoment)
                .Skip(skip)
                .Take(pagination.Size)
                .Select(preSale => new PreSaleResult
                {
                    CreatedMoment = preSale.CreatedMoment,
                    Description = preSale.Description,
                    EndSellingAt = preSale.EndSellingAt,
                    LogoUrl = preSale.LogoUrl,
                    MaxPerOrder = preSale.MaxPerOrder,
                    MinPerOrder = preSale.MinPerOrder,
                    ModifiedMoment = preSale.ModifiedMoment,
                    Name = preSale.Name,
                    PreSaleReference = preSale.PreSaleReference,
                    Price = preSale.Price,
                    ReleaseSchedule = preSale.ReleaseSchedule,
                    StartSellingAt = preSale.StartSellingAt,
                    State = preSale.State,
                    Symbol = preSale.Symbol,
                    TotalSupply = preSale.TotalSupply
                })
                .ToListAsync();

            return new PreSaleListResult
            {
                Data = data,
                TotalCount = totalCount,
                PageCount = pageCount
            };
        }

        public async Task SyncExpirePreSaleTokenAsync()
        {
            var now = DateTime.UtcNow;


            var expiredPreSales = await _preSaleRepository
                .AsQueryable()
                .Where(x => x.State == PreSaleState.Active && x.EndSellingAt < now)
                .ToListAsync();

            if (!expiredPreSales.Any())
                return;


            foreach (var preSale in expiredPreSales)
            {
                var update = Builders<PreSale>.Update
                    .Set(x => x.State, PreSaleState.Expired);

                await _preSaleRepository.FindOneAndUpdateAsync(
                    x => x.PreSaleReference == preSale.PreSaleReference,
                    update
                );

                await SyncPreSaleToStorageAsync(preSale.PreSaleReference);
            }
        }

        public async Task SyncPreSaleToStorageAsync(string preSaleReference)
        {
            var presale = await _preSaleRepository
                .AsQueryable()
                .FirstOrDefaultAsync(x => x.PreSaleReference == preSaleReference)
                ?? throw new NotFoundException("PreSale not found!");


            var totalSupplied = await _preSaleOrderRepository
                    .AsQueryable()
                    .Where(q => q.PreSaleReference == preSaleReference)
                    .Where(q => q.State == PreSaleOrderState.InProgress || q.State == PreSaleOrderState.Completed)
                    .SumAsync(x => (decimal?)x.ReceivingTokenAmount) ?? 0;

            var data = new PreSaleData
            {
                CreatedMoment = presale.CreatedMoment,
                ModifiedMoment = presale.ModifiedMoment,
                PreSaleReference = presale.PreSaleReference,
                Name = presale.Name,
                Symbol = presale.Symbol,
                LogoUrl = presale.LogoUrl,
                Description = presale.Description,
                TotalSupply = presale.TotalSupply,
                MaxPerOrder = presale.MaxPerOrder,
                MinPerOrder = presale.MinPerOrder,
                TotalSupplied = totalSupplied,
                AvailableForEachOrder = Math.Min(presale.TotalSupply - totalSupplied, presale.MaxPerOrder),
                Price = presale.Price,
                StartSellingAt = presale.StartSellingAt,
                EndSellingAt = presale.EndSellingAt,
                ReleaseSchedule = presale.ReleaseSchedule,
                State = presale.State,
                LastUpdated = DateTime.UtcNow
            };

            _preSaleStorage.Upsert(preSaleReference, data);
        }

        public async Task SyncPreSaleToStorageAsync(PreSale presale)
        {

            var preSaleReference = presale.PreSaleReference;
            var totalSupplied = await _preSaleOrderRepository
                    .AsQueryable()
                    .Where(q => q.PreSaleReference == preSaleReference)
                    .Where(q => q.State == PreSaleOrderState.InProgress || q.State == PreSaleOrderState.Completed)
                    .SumAsync(x => (decimal?)x.ReceivingTokenAmount) ?? 0;

            var data = new PreSaleData
            {
                CreatedMoment = presale.CreatedMoment,
                ModifiedMoment = presale.ModifiedMoment,
                PreSaleReference = presale.PreSaleReference,
                Name = presale.Name,
                Symbol = presale.Symbol,
                LogoUrl = presale.LogoUrl,
                Description = presale.Description,
                TotalSupply = presale.TotalSupply,
                MaxPerOrder = presale.MaxPerOrder,
                MinPerOrder = presale.MinPerOrder,
                TotalSupplied = totalSupplied,
                AvailableForEachOrder = Math.Min(presale.TotalSupply - totalSupplied, presale.MaxPerOrder),
                Price = presale.Price,
                StartSellingAt = presale.StartSellingAt,
                EndSellingAt = presale.EndSellingAt,
                ReleaseSchedule = presale.ReleaseSchedule,
                State = presale.State,
                LastUpdated = DateTime.UtcNow
            };

            _preSaleStorage.Upsert(preSaleReference, data);
        }

        public async Task<PreSale> GetPreSaleDataByReferenceForInternalUsageAsync(string preSaleReference)
        {
            return await _preSaleRepository.AsQueryable().FirstOrDefaultAsync(q => q.PreSaleReference == preSaleReference && q.State == PreSaleState.Active) ??
                throw new NotFoundException("Active PreSale token not found!");
        }


        public async Task InitializePreSaleStorageAsync()
        {

            var presales = await _preSaleRepository
                .AsQueryable().
                Where(q => q.State == PreSaleState.Active || q.State == PreSaleState.Expired)
                .ToListAsync();

            if (presales == null || presales.Count == 0)
            {
                _preSaleStorage.Init(new List<PreSaleData>());
                return;
            }


            var ordersGrouped = await _preSaleOrderRepository
                .AsQueryable()
                .Where(x =>
                    x.State == PreSaleOrderState.InProgress ||
                    x.State == PreSaleOrderState.Completed)
                .GroupBy(x => x.PreSaleReference)
                .Select(g => new
                {
                    PreSaleReference = g.Key,
                    TotalSupplied = g.Sum(x => (decimal?)x.ReceivingTokenAmount) ?? 0
                })
                .ToListAsync();

            var ordersDict = ordersGrouped
                .ToDictionary(x => x.PreSaleReference, x => x.TotalSupplied);


            var storageList = new List<PreSaleData>();

            var allbalances = await _blockChainService.GetPreSaleContractBalancesAsync();

            foreach (var presale in presales)
            {
                ordersDict.TryGetValue(presale.PreSaleReference, out var totalSupplied);

                var availableForEachOrder =
                    Math.Min(
                        presale.TotalSupply - totalSupplied,
                        presale.MaxPerOrder
                    );

                var balance = allbalances[presale.Symbol];

                var data = new PreSaleData
                {
                    CreatedMoment = presale.CreatedMoment,
                    ModifiedMoment = presale.ModifiedMoment,
                    PreSaleReference = presale.PreSaleReference,
                    Name = presale.Name,
                    Symbol = presale.Symbol,
                    LogoUrl = presale.LogoUrl,
                    Description = presale.Description,

                    TotalSupply = presale.TotalSupply,
                    MaxPerOrder = presale.MaxPerOrder,
                    MinPerOrder = presale.MinPerOrder,

                    TotalSupplied = totalSupplied,
                    AvailableForEachOrder = availableForEachOrder,

                    Price = presale.Price,
                    StartSellingAt = presale.StartSellingAt,
                    EndSellingAt = presale.EndSellingAt,
                    ReleaseSchedule = presale.ReleaseSchedule,
                    ContractBalance = balance,
                    State = presale.State,
                    LastUpdated = DateTime.UtcNow
                };

                storageList.Add(data);
            }

            _preSaleStorage.Init(storageList);
        }



        public async Task SyncPreSaleTokenBalanceAsync(string tokenName)
        {
            var balance = await _blockChainService.GetPreSaleContractSingleBalanceAsync(tokenName);
            _preSaleStorage.UpdateContractBalance(tokenName, balance);
        }

        //public async Task SyncCompletedPreSalesAsync()
        //{
        //    var now = DateTime.UtcNow;

        //    var presales = await _preSaleRepository.AsQueryable()
        //        .Where(x => x.State == PreSaleState.Expired)
        //        .Where(x => !x.IsOwnOrdersCompleted)
        //        .Where(x => x.ReleaseSchedule != null && x.ReleaseSchedule.Any())
        //        .Where(x => x.ReleaseSchedule.Max(r => r.ReleaseDate) <= now)
        //        .ToListAsync();

        //    foreach (var presale in presales)
        //    {
        //        await _preSaleOrderService
        //            .MakeCompeletePreSaleOrderStateByPreSaleReferenceAsync(presale.PreSaleReference);

        //        presale.IsOwnOrdersCompleted = true;
        //        await _preSaleRepository.ReplaceOneAsync(presale);
        //    }
        //}






        #region Privates


        private PreSaleResult MapToResult(PreSale entity)
        {
            return new PreSaleResult
            {
                PreSaleReference = entity.PreSaleReference,
                Name = entity.Name,
                Symbol = entity.Symbol,
                LogoUrl = entity.LogoUrl,
                Description = entity.Description,
                TotalSupply = entity.TotalSupply,
                MaxPerOrder = entity.MaxPerOrder,
                MinPerOrder = entity.MinPerOrder,
                Price = entity.Price,
                StartSellingAt = entity.StartSellingAt,
                EndSellingAt = entity.EndSellingAt,
                ReleaseSchedule = entity.ReleaseSchedule,
                State = entity.State,
                CreatedMoment = entity.CreatedMoment,
                ModifiedMoment = entity.ModifiedMoment
            };
        }

        private void ValidateCreateRequest(CreatePreSaleTokenUpdate update)
        {
            if (update == null)
                throw new Exception("Request is null.");
            ValidateToken(update.Symbol);
            ValidatePerOrderAmounts(update);
            ValidateTimeRange(update);
            ValidateReleaseSchedule(update);
        }

        private void ValidatePerOrderAmounts(CreatePreSaleTokenUpdate update)
        {
            if (update.MinPerOrder <= 0)
                throw new Exception("MinPerOrder must be greater than zero.");

            if (update.MaxPerOrder <= 0)
                throw new Exception("MaxPerOrder must be greater than zero.");

            if (update.MinPerOrder > update.MaxPerOrder)
                throw new Exception("MinPerOrder cannot be greater than MaxPerOrder.");

            if (update.MaxPerOrder > update.TotalSupply)
                throw new Exception("MaxPerOrder cannot be greater than TotalSupply.");
        }

        private void ValidateTimeRange(CreatePreSaleTokenUpdate update)
        {
            if (update.StartSellingAt >= update.EndSellingAt)
                throw new Exception("StartSellingAt must be earlier than EndSellingAt.");

            if (update.StartSellingAt <= DateTime.UtcNow.AddDays(-1))
                throw new Exception("StartSellingAt must be in the future.");
        }

        private void ValidateReleaseSchedule(CreatePreSaleTokenUpdate update)
        {
            if (update.ReleaseSchedule == null || !update.ReleaseSchedule.Any())
                throw new Exception("Release schedule is required.");

            var totalPercentage = update.ReleaseSchedule.Sum(x => x.Percentage);

            if (Math.Round(totalPercentage, 2) != 100)
                throw new Exception("Release schedule percentages must sum to 100.");

            foreach (var step in update.ReleaseSchedule)
            {
                if (step.Percentage <= 0)
                    throw new Exception("Each release step percentage must be greater than zero.");

                if (step.ReleaseDate <= update.StartSellingAt)
                    throw new Exception("Release dates must be after StartSellingAt.");
            }

            var ordered = update.ReleaseSchedule.OrderBy(x => x.ReleaseDate).ToList();
            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].ReleaseDate == ordered[i - 1].ReleaseDate)
                    throw new Exception("Release dates must be unique.");
            }
        }


        private AvailableTokenData ValidateToken(string tokenName)
        {

            if (tokenName == null)
                throw new BadRequestException($"Unsupported token name! {tokenName}");

            var tokenData = _availableTokenData.FirstOrDefault(q => q.Name.Equals(tokenName, StringComparison.OrdinalIgnoreCase))
                ?? throw new BadRequestException($"Unsupported token name! {tokenName}");
            return tokenData;
        }



        #endregion




    }
}