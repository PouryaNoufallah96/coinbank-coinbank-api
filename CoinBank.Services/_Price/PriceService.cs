using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._Price.DTOs.Results;
using CoinBank.Services._Price.DTOs.Storages;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using Utilities.Exceptions.Common;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Price
{
    public class PriceService(
        AvailableTokensSettings _availableTokenDatas,
       ILogger<PriceService> _logger,
       PriceStorage _priceStorage) : IPriceService, IScopedDependency
    {
        private static readonly HttpClient _httpClient = new HttpClient();
         

        public async Task<PriceResult> FetchTokenPriceForShieldAsync(string tokenName)
        {
            var priceData = _priceStorage.GetPrice(tokenName.ToUpper());
            if (priceData != null && priceData.Price != null) return priceData.Price;
            return await FetchTokenPriceAsync(tokenName.ToUpper());
        }

        public async Task<PriceResult> FetchTokenPriceAsync(string tokenName)
        {
            try
            {
                return await FetchTokenPriceFromGeckoTerminalAsync(tokenName);

            }
            catch (Exception ex)
            {
                await Task.Delay(30000);
                return await FetchTokenPriceFromGeckoTerminalAsync(tokenName);
            }
        }

        public async Task FetchAllPricesAsync()
        {
            foreach (var token in _availableTokenDatas)
            {
                var priceData = await FetchTokenPriceAsync(token.Name);
                if (priceData != null)
                {
                    _priceStorage.UpdatePrice(token.Name, priceData);
                }

                await Task.Delay(12000);
            }
        }
    
        public async Task<Dictionary<string, PriceResult>> FetchAllPricesForInternalUsageAsync()
        {
            var result = new ConcurrentDictionary<string, PriceResult>();

            var tasks = _availableTokenDatas.Select(async token =>
            {
                var priceData = await FetchTokenPriceAsync(token.Name);
                if (priceData != null)
                {
                    result[token.Name] = priceData;
                }
            });

            await Task.WhenAll(tasks);

            return result.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public async Task<decimal> GetOneTokenPriceForInternalUsage(string tokenName)
        {

            decimal price = 0;

            if (!_priceStorage.TryGetValue(tokenName.ToUpper(), out var value) || value == null || value.Price == null)
            {
                var data  = await FetchTokenPriceAsync(tokenName);
                price = data.Price;
            }

            price = value.Price.Price;
            return price;
        }

        public async Task<EffectivePriceResult> CalculateEffectivePriceAsync(string tokenName, decimal assetQuantity, decimal USDTAmount)
        {
            // Validate input parameters
            if (assetQuantity <= 0) throw new BadRequestException("Token quantity must be greater than zero.", nameof(assetQuantity));
            var priceData = await FetchTokenPriceAsync(tokenName);


            if (priceData == null)
            {
                if (!_priceStorage.TryGetValue(tokenName.ToUpper(), out var value) || value == null || value.Price == null)
                {
                    throw new BadRequestException(nameof(priceData), "Price data cannot be null.");
                }

                priceData = value.Price;
            }

            try
            {
                var price = priceData.Price;
                var effectivePrice = USDTAmount / assetQuantity;
                var priceImpact = (effectivePrice - priceData.Price) / priceData.Price * 100;
                return new EffectivePriceResult
                {
                    EffectivePrice = effectivePrice,
                    Price = price,
                    Impact = priceImpact
                };

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error calculating effective price for {Token}", priceData.TokenName);
                throw new BaseException("Failed to calculate effective price due to an unexpected error.");
            }
        }


        /// <summary>
        /// this method use for fetch price data with token name
        /// </summary>
        /// <param name="tokenName"></param>
        /// <param name="poolId"></param>
        /// <returns></returns>
        public async Task<PriceResult> FetchTokenPriceFromGeckoTerminalAsync(string tokenName, string poolId = null)
        {
            var pool = poolId == null ? _availableTokenDatas.FirstOrDefault(q => q.Name == tokenName.ToUpper()).PoolId : poolId;
            string url = $"https://api.geckoterminal.com/api/v2/networks/bsc/pools/{pool}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(jsonString);

                var attributes = doc.RootElement
                    .GetProperty("data")
                    .GetProperty("attributes");

                decimal basePrice = decimal.Parse(attributes.GetProperty("base_token_price_usd").GetString()!);
                decimal quotePrice = decimal.Parse(attributes.GetProperty("quote_token_price_usd").GetString()!);

                decimal liquidityUsd = decimal.Parse(attributes.GetProperty("reserve_in_usd").GetString()!);
                decimal volume24h = decimal.Parse(attributes.GetProperty("volume_usd").GetProperty("h24").GetString()!);
                decimal changePrice24h = decimal.Parse(attributes.GetProperty("price_change_percentage").GetProperty("h24").GetString()!);
                decimal? poolFee = attributes.TryGetProperty("pool_fee_percentage", out var feeProp) && feeProp.ValueKind != JsonValueKind.Null
                                   ? decimal.Parse(feeProp.GetString()!)
                                   : null;

                return new PriceResult
                {
                    TokenName = tokenName,
                    TokenNetwork = "BSC",
                    Price = basePrice,
                    ChangePrice24hPercentage = changePrice24h,
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching token price gecko for {poolId}: {ex.Message}");
                return null;
            }
        }

    }
}
