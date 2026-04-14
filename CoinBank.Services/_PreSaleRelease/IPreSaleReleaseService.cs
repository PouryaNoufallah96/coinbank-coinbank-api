using CoinBank.Services._PreSaleRelease.DTOs.Results;

namespace CoinBank.Services._PreSaleRelease
{
    public interface IPreSaleReleaseService
    {
        Task<List<ReleasesOfPreSaleOrderResult>> GetReleasesOfPreSaleOrderByReferenceAsync(string preSaleOrderReference);
    }
}
