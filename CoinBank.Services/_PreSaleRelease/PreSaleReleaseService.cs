//using CoinBank.Domain.Collections;
//using CoinBank.Domain.Repositories.Contracts;
//using CoinBank.Services._PreSaleRelease.DTOs.Results;
//using MongoDB.Driver;
//using MongoDB.Driver.Linq;
//using static Utilities.Constants.RegisterMode;

//namespace CoinBank.Services._PreSaleRelease
//{
//    public class PreSaleReleaseService(IPreSaleReleaseRepository _preSaleReleaseRepository) : IPreSaleReleaseService, IScopedDependency
//    {
//        public async Task<List<ReleasesOfPreSaleOrderResult>> GetReleasesOfPreSaleOrderByReferenceAsync(string preSaleOrderReference)
//        {
//            var data = await _preSaleReleaseRepository
//                .AsQueryable()
//                .Where(q => q.PreSaleOrderReference == preSaleOrderReference)
//                .ToListAsync();

//            var result = data.Select(x => ConvertToResult(x)).ToList();
//            return result;
//        }

//        public ReleasesOfPreSaleOrderResult ConvertToResult(PreSaleRelease x)
//        {
//            return new ReleasesOfPreSaleOrderResult
//            {
//                PreSaleReleaseReference = x.PreSaleReleaseReference,
//                PreSaleOrderReference = x.PreSaleOrderReference,
//                WalletAddress = x.WalletAddress,
//                TokenSymbol = x.TokenSymbol,
//                ReleasePercentage = x.ReleasePercentage,
//                ReleaseAmount = x.ReleaseAmount,
//                ScheduledAt = x.ScheduledAt,
//                TransactionHash = x.TransactionHash,
//                CreatedMoment = x.CreatedMoment,
//                ModifiedMoment = x.ModifiedMoment
//            };
//        }



//    }
//}
