using CoinBank.Services._Price.DTOs.Results;

namespace CoinBank.Services._Price
{
    public interface IPriceService
    {
        Task<PriceResult> FetchTokenPriceAsync(string tokenName);
        Task FetchAllPricesAsync();
        Task<Dictionary<string, PriceResult>> FetchAllPricesForInternalUsageAsync();
        Task<decimal> GetOneTokenPriceForInternalUsage(string tokenName);
        Task<EffectivePriceResult> CalculateEffectivePriceAsync(string tokenName, decimal assetQuantity, decimal USDTAmount);
    }
}
