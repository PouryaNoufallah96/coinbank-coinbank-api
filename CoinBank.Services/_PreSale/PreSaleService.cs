using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._PreSale.DTOs.Results;
using CoinBank.Services._PreSale.DTOs.Updates;
using CoinBank.Services._PreSaleOrder;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Utilities.DTOs;
using Utilities.Exceptions.Common;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._PreSale
{
    public class PreSaleService(IPreSaleRepository _preSaleRepository,IPreSaleOrderService _preSaleOrderService) : IPreSaleService, IScopedDependency
        
    {

        public async Task<PreSaleResult> CreatePreSaleTokenAsync(CreatePreSaleTokenUpdate update)
        {
            ValidateCreateRequest(update);
            var symbol = update.Symbol.Trim().ToUpper();

            var existing = await _preSaleRepository.AsQueryable().FirstOrDefaultAsync(q => q.Symbol== symbol);

            if (existing != null)
                throw new BadRequestException("PreSale with this symbol already exists.");

            var newPreSale = new PreSale
            {
                PreSaleReference = Guid.NewGuid().ToString("N"),
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
                RegisterMoment = DateTime.UtcNow
            };

            //TODO : send to blockChain For submit
            newPreSale.RegisterHash = "";
            newPreSale.RegisterMoment = DateTime.UtcNow;
            newPreSale.State = PreSaleState.Active;

            await _preSaleRepository.InsertOneAsync(newPreSale);

            return MapToResult(newPreSale);
        }

        public async Task<PreSaleResult> GetOnePreSaleTokenAsync(GetOnePreSaleTokenUpdate update)
        {
            var preSale = await _preSaleRepository.AsQueryable().FirstOrDefaultAsync(q => q.PreSaleReference == update.PreSaleReference) ??
                throw new NotFoundException("PreSale not found.");

            return MapToResult(preSale);
        }

        public async Task<PreSaleListResult> GetAllPreSaleTokensAsync(Pagination pagination)
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
                   Description  = preSale.Description,
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
                .ToListAsync(cancellationToken);

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

            var builder = Builders<PreSale>.Filter;

            var filter = builder.And(
                builder.Eq(x => x.State, PreSaleState.Active),
                builder.Lt(x => x.EndSellingAt, now)
            );

            var update = Builders<PreSale>.Update
                .Set(x => x.State, PreSaleState.Expired);

            await _preSaleRepository.UpdateManyAsync(filter, update);
        }


        public async Task SyncCompletedPreSalesAsync()
        {
            var now = DateTime.UtcNow;

            var presales = await _preSaleRepository.AsQueryable()
                .Where(x => x.State == PreSaleState.Expired)
                .Where(x => !x.IsOwnOrdersCompleted)
                .Where(x => x.ReleaseSchedule != null && x.ReleaseSchedule.Any())
                .Where(x => x.ReleaseSchedule.Max(r => r.ReleaseDate) <= now)
                .ToListAsync();

            foreach (var presale in presales)
            {
                await _preSaleOrderService
                    .MakeCompeletePreSaleOrderStateByPreSaleReferenceAsync(presale.PreSaleReference);

                presale.IsOwnOrdersCompleted = true;
                await _preSaleRepository.ReplaceOneAsync(presale);
            }
        }



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
                CreatedMoment = entity.RegisterMoment,
                ModifiedMoment = entity.ModifiedMoment
            };
        }

        private void ValidateCreateRequest(CreatePreSaleTokenUpdate update)
        {
            if (update == null)
                throw new Exception("Request is null.");

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

            if (update.StartSellingAt <= DateTime.UtcNow.AddMinutes(-1))
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

     

        #endregion




    }
}