using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._PreSale.DTOs.Results;
using CoinBank.Services._PreSale.DTOs.Updates;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Utilities.DTOs;
using Utilities.Exceptions.Common;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._PreSale
{
    public class PreSaleService(IPreSaleRepository _preSaleRepository) : IPreSaleService, IScopedDependency
    {

        public async Task<PreSaleResult> CreatePreSaleTokenAsync(CreatePreSaleTokenUpdate update)
        {
            ValidateCreateRequest(update);

            var existing = await _preSaleRepository.AsQueryable().FirstOrDefaultAsync(q => q.Symbol.ToLower() == update.Symbol.ToLower());

            if (existing != null)
                throw new BadRequestException("PreSale with this symbol already exists.");

            var newPreSale = new PreSale
            {
                PreSaleReference = Guid.NewGuid().ToString("N"),
                Name = update.Name,
                Symbol = update.Symbol,
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
            
            var entity = await _preSaleRepository.AsQueryable().FirstOrDefaultAsync(q => q.PreSaleReference == update.PreSaleReference) ??
                throw new NotFoundException("PreSale not found.");
            

            return MapToResult(entity);
        }

        public async Task<PreSaleListResult> GetAllPreSaleTokensAsync(Pagination pagination)
        {
            var totalCount = await _preSaleRepository.CountAsync();

            var data = await _preSaleRepository.GetPagedAsync(pagination.Page, pagination.Size);

            return new PreSaleListResult
            {
                Data = data.Select(MapToResult).ToList(),
                TotalCount = totalCount,
                PageCount = (int)Math.Ceiling((double)totalCount / pagination.Size)
            };
        }

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



        #region Privates

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