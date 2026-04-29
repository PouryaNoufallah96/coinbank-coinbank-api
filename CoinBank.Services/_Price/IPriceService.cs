using CoinBank.Services._Price.DTOs.Results;

namespace CoinBank.Services._Price
{
    public interface IPriceService
    {
        Task<PriceResult> FetchTokenPriceFromGeckoTerminalAsync(string tokenName, string poolId = null);
        Task FetchAllPricesAsync();
        Task<decimal> GetOneTokenPriceForInternalUsageAsync(string tokenName);
        Task<EffectivePriceResult> CalculateEffectivePriceAsync(string tokenName, decimal assetQuantity, decimal USDTAmount);

        Task<PriceResult> FetchTokenPriceFromCoinMarketCapAsync(string symbol);
        Task<List<PriceResult>> FetchTokensPriceFromCoinMarketCapAsync(List<string> symbols);
        Task<List<PriceResult>> SyncAllPricesFromCoinMarketCapAsync();
    }
}
