using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._Price.DTOs.Results;
using CoinBank.Services._Price.DTOs.Settings;
using CoinBank.Services._Price.DTOs.Storages;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Utilities.Exceptions.Common;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Price
{
    public class PriceService(
       AvailableTokensSettings _availableTokenDatas,
       ILogger<PriceService> _logger,
       PriceSetting _priceSetting,
       CoinHistoryStorage _coinHistoryStorage,
       PriceStorage _priceStorage) : IPriceService, IScopedDependency
    {
        private static readonly HttpClient _httpClient = new HttpClient();


        public async Task FetchAllPricesAsync()
        {
            try
            {
                var result = await SyncAllPricesFromCoinMarketCapAsync();
                foreach (var item in result)
                {
                    if (item != null)
                    {
                        _priceStorage.UpdatePrice(item.TokenName, item);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error fetching token price from CoinMarketCap for : {e.Message}");

                var result = await SyncAllPricesFromGeckoTerminalAsync();
                foreach (var item in result)
                {
                    if (item != null)
                    {
                        _priceStorage.UpdatePrice(item.TokenName, item);
                    }
                }
            }


        }

        public async Task<decimal> GetOneTokenPriceForInternalUsageAsync(string tokenName)
        {
            tokenName = tokenName.ToUpper();

            if (_priceStorage.TryGetValue(tokenName, out var value)
                && value != null
                && value.Price != null)
            {
                return value.Price.Price;
            }

            decimal price = 0;

            try
            {
                var data = await FetchTokenPriceFromCoinMarketCapAsync(tokenName);

                if (data != null)
                {
                    price = data.Price;
                    return price;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching token price from CoinMarketCap for {tokenName}: {ex.Message}");
            }

            try
            {
                var data = await FetchTokenPriceFromGeckoTerminalAsync(tokenName);

                if (data != null)
                {
                    price = data.Price;
                    return price;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching token price from GeckoTerminal for {tokenName}: {ex.Message}");
            }

            return 0;
        }


        #region GeckoTerminal

        /// <summary>
        /// this method use for fetch price data with token name
        /// </summary>
        /// <param name="tokenName"></param>
        /// <param name="poolId"></param>
        /// <returns></returns>
        public async Task<PriceResult> FetchTokenPriceFromGeckoTerminalAsync(string tokenName, string poolId = null)
        {
            var pool = poolId == null ? _availableTokenDatas.FirstOrDefault(q => q.Name == tokenName.ToUpper()).PoolId : poolId;
            var network = _availableTokenDatas.FirstOrDefault(q => q.Name == tokenName.ToUpper()).Network;
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
                    TokenNetwork = network,
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

        public async Task<List<PriceResult>> SyncAllPricesFromGeckoTerminalAsync()
        {
            var results = new List<PriceResult>();

            try
            {
                var tokens = _availableTokenDatas
                    .Where(t => t.SyncPrice)
                    .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                    .ToList();

                var baseTokens = new List<AvailableTokenData>
                {
                    new AvailableTokenData
                    {
                        Name = "BNB",
                        Network = "BSC",
                        PoolId = "0x58f876857a02d6762cfc6c6e3f2c0b8c0e0f9e7c", // PancakeSwap BNB/USDT
                        PriceDecimalPlaces = 4
                    },
                    new AvailableTokenData
                    {
                        Name = "ETH",
                        Network = "ERC20",
                        PoolId = "0x60594a405d53811d3bc4766596efd80fd545a270", // Uniswap ETH/USDT
                        PriceDecimalPlaces = 4
                    },
                    //new AvailableTokenData
                    //{
                    //    Name = "TRX",
                    //    Network = "TRC20",
                    //    PoolId = "TRX_USDT_POOL_ID",
                    //    PriceDecimalPlaces = 4
                    //},
                    new AvailableTokenData
                    {
                        Name = "USDT",
                        Network = "MULTI",
                        PoolId = "0x16b9a828c5a7a5c5e7c0c9f8b1b1f2d5c9e9e7f1", // USDT/USDC 
                        PriceDecimalPlaces = 4
                    }
                };

                var allTokens = tokens
                    .Concat(baseTokens)
                    .GroupBy(t => new { Name = t.Name.ToUpper(), t.Network })
                    .Select(g => g.First())
                    .ToList();

                foreach (var token in allTokens)
                {
                    try
                    {
                        var priceData = await FetchTokenPriceFromGeckoTerminalAsync(token.Name, token.PoolId);

                        if (priceData != null)
                        {
                            priceData.Price = Math.Round(priceData.Price, token.PriceDecimalPlaces);
                            priceData.TokenNetwork = token.Network;
                            results.Add(priceData);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error fetching Gecko price for {token.Name}: {ex.Message}");
                    }


                    await Task.Delay(1500);
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error syncing all prices from Gecko: {ex.Message}");
                return results;
            }
        }

        #endregion


        #region CoinMarketCap Price
        public async Task<List<PriceResult>> FetchTokensPriceFromCoinMarketCapAsync(List<string> symbols)
        {
            if (symbols == null || !symbols.Any())
                return new List<PriceResult>();

            string symbolQuery = string.Join(",", symbols.Select(s => s.ToUpper()));
            string url = $"https://pro-api.coinmarketcap.com/v1/cryptocurrency/quotes/latest?symbol={symbolQuery}&convert=USD";

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-CMC_PRO_API_KEY", _priceSetting.CMCApiKey);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(jsonString);

                var data = doc.RootElement.GetProperty("data");

                var results = new List<PriceResult>();

                foreach (var symbol in symbols)
                {
                    if (!data.TryGetProperty(symbol.ToUpper(), out var tokenData))
                        continue;

                    var quote = tokenData.GetProperty("quote").GetProperty("USD");

                    decimal price = quote.GetProperty("price").GetDecimal();
                    decimal change24h = quote.GetProperty("percent_change_24h").GetDecimal();

                    results.Add(new PriceResult
                    {
                        TokenName = symbol,
                        TokenNetwork = null,
                        Price = price,
                        ChangePrice24hPercentage = change24h
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching token prices from CoinMarketCap: {ex.Message}");
                return new List<PriceResult>();
            }
        }

        public async Task<PriceResult> FetchTokenPriceFromCoinMarketCapAsync(string symbol)
        {
            string url = $"https://pro-api.coinmarketcap.com/v1/cryptocurrency/quotes/latest?symbol={symbol.ToUpper()}&convert=USDT";

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-CMC_PRO_API_KEY", _priceSetting.CMCApiKey);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(jsonString);

                var root = doc.RootElement.GetProperty("data").GetProperty(symbol.ToUpper());
                var quote = root.GetProperty("quote").GetProperty("USDT");

                decimal price = quote.GetProperty("price").GetDecimal();
                decimal change24h = quote.GetProperty("percent_change_24h").GetDecimal();

                return new PriceResult
                {
                    TokenName = symbol,
                    TokenNetwork = "BSC",
                    Price = price,
                    ChangePrice24hPercentage = change24h
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching token price from CoinMarketCap for {symbol}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<PriceResult>> SyncAllPricesFromCoinMarketCapAsync()
        {
            try
            {
                var tokens = _availableTokenDatas
                    .Where(t => t.SyncPrice)
                    .Where(t => t.CMCID > 0)
                    .ToList();

                var baseTokens = new List<AvailableTokenData>
                {
                    new() { Name = "BNB", Network = "BSC", PriceDecimalPlaces = 4, CMCID = 1839 },
                    new() { Name = "ETH", Network = "ERC20", PriceDecimalPlaces = 4, CMCID = 1027 },
                    new() { Name = "TRX", Network = "TRC20", PriceDecimalPlaces = 4, CMCID = 1958 },
                    new() { Name = "USDT", Network = "MULTI", PriceDecimalPlaces = 4, CMCID = 825 }
                };

                var allTokens = tokens
                    .Concat(baseTokens)
                    .GroupBy(t => new { t.CMCID, t.Network })
                    .Select(g => g.First())
                    .ToList();

                if (!allTokens.Any())
                    return new List<PriceResult>();

                var ids = allTokens
                    .Select(t => t.CMCID)
                    .Distinct()
                    .ToList();

                string idQuery = string.Join(",", ids);

                string url =
                    $"https://pro-api.coinmarketcap.com/v2/cryptocurrency/quotes/latest?id={idQuery}&convert=USD";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-CMC_PRO_API_KEY", _priceSetting.CMCApiKey);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();

                using JsonDocument doc = JsonDocument.Parse(jsonString);

                var data = doc.RootElement.GetProperty("data");

                var results = new List<PriceResult>();

                foreach (var token in allTokens)
                {
                    var id = token.CMCID.ToString();

                    if (!data.TryGetProperty(id, out var tokenData))
                        continue;

                    var quote = tokenData
                        .GetProperty("quote")
                        .GetProperty("USD");

                    decimal price = quote.GetProperty("price").GetDecimal();
                    decimal change24h = quote.GetProperty("percent_change_24h").GetDecimal();

                    results.Add(new PriceResult
                    {
                        TokenName = token.Name,
                        TokenNetwork = token.Network,
                        Price = Math.Round(price, token.PriceDecimalPlaces),
                        ChangePrice24hPercentage = change24h
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing all prices");
                return new List<PriceResult>();
            }
        }

        public async Task<List<PriceResult>> SyncAllPricesFromCoinMarketCapWithSybmolsAsync()
        {
            try
            {
                var tokens = _availableTokenDatas
                    .Where(t => t.SyncPrice)
                    .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                    .ToList();

                var baseTokens = new List<AvailableTokenData>
                {
                    new AvailableTokenData { Name = "BNB", Network = "BSC", PriceDecimalPlaces = 4 ,CMCID = 1839 },
                    new AvailableTokenData { Name = "ETH", Network = "ERC20", PriceDecimalPlaces = 4 ,CMCID = 1027 },
                    new AvailableTokenData { Name = "TRX", Network = "TRC20", PriceDecimalPlaces = 4 , CMCID = 1958 },
                    new AvailableTokenData { Name = "USDT", Network = "MULTI", PriceDecimalPlaces = 4 , CMCID = 825 }
                };

                var allTokens = tokens
                    .Concat(baseTokens)
                    .GroupBy(t => new { Name = t.Name.ToUpper(), t.Network })
                    .Select(g => g.First())
                    .ToList();

                if (!allTokens.Any())
                    return new List<PriceResult>();

                var symbols = allTokens
                    .Select(t => t.Name.ToUpper())
                    .Distinct()
                    .ToList();

                string symbolQuery = string.Join(",", symbols);
                string url = $"https://pro-api.coinmarketcap.com/v1/cryptocurrency/quotes/latest?symbol={symbolQuery}&convert=USD";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-CMC_PRO_API_KEY", _priceSetting.CMCApiKey);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var jsonString = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(jsonString);

                var data = doc.RootElement.GetProperty("data");

                var results = new List<PriceResult>();

                foreach (var token in allTokens)
                {
                    var symbol = token.Name.ToUpper();

                    if (!data.TryGetProperty(symbol, out var tokenData))
                        continue;

                    var quote = tokenData.GetProperty("quote").GetProperty("USD");

                    decimal price = quote.GetProperty("price").GetDecimal();
                    decimal change24h = quote.GetProperty("percent_change_24h").GetDecimal();

                    results.Add(new PriceResult
                    {
                        TokenName = symbol,
                        TokenNetwork = token.Network,
                        Price = Math.Round(price, token.PriceDecimalPlaces),
                        ChangePrice24hPercentage = change24h
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing all prices");
                return new List<PriceResult>();
            }
        }

        #endregion

        #region CoinMarketCap Historical

        public async Task SyncCoinHistoryTokenFromCoinMarketCapAsync()
        {
            var token = _availableTokenDatas.FirstOrDefault(q => q.Name == "COINBANK");
            if (token == null) return;

            await GetCoinHistoryAsync(token.Name, token.CMCID);
        }

        private async Task<CoinHistoryData> GetCoinHistoryAsync(string coinName, long coinId)
        {
           
            var end = DateTime.UtcNow;
            var start = end.AddDays(-365);

            var jsonString = await GetOhlcvHistoricalAsync(coinId, start, end);

            if (string.IsNullOrEmpty(jsonString))
            {
                _logger.LogWarning("No OHLCV data returned for coinId: {CoinId}", coinId);

                return new CoinHistoryData
                {
                    Daily = new(),
                    Weekly = new(),
                    Annually = new()
                };
            }

            using var doc = JsonDocument.Parse(jsonString);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("quotes", out var quotes))
            {
                return new CoinHistoryData();
            }

            var parsed = Parse(quotes);

            var result = new CoinHistoryData
            {
                Daily = BuildDaily(parsed),
                Weekly = BuildWeekly(parsed),
                Annually = BuildMonthly(parsed)
            };

            _coinHistoryStorage[coinName] = result;

            return result;
        }

        private async Task<string?> GetOhlcvHistoricalAsync(long coinId, DateTime start, DateTime end)
        {
            string url =
                $"https://pro-api.coinmarketcap.com/v2/cryptocurrency/ohlcv/historical" +
                $"?id={coinId}&time_start={start:yyyy-MM-dd}&time_end={end:yyyy-MM-dd}";

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-CMC_PRO_API_KEY", _priceSetting.CMCApiKey);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error fetching OHLCV historical data from CoinMarketCap for coinId: {CoinId}",
                    coinId);

                return null;
            }
        }

        private List<CoinCandle> BuildDaily(List<CoinCandle> data)
        {
            return data
                .OrderBy(x => x.Time)
                .TakeLast(30)
                .ToList();
        }

        private List<CoinCandle> BuildWeekly(List<CoinCandle> data)
        {
            return data
                .OrderBy(x => x.Time)
                .GroupBy(x => GetWeekStart(x.Time))
                .Select(g => new CoinCandle
                {
                    Time = g.Key,
                    Open = g.First().Open,
                    Close = g.Last().Close,
                    High = g.Max(x => x.High),
                    Low = g.Min(x => x.Low),
                    Volume = g.Sum(x => x.Volume)
                })
                .ToList();
        }

        private List<CoinCandle> BuildMonthly(List<CoinCandle> data)
        {
            var grouped = data
                .OrderBy(x => x.Time)
                .GroupBy(x => new { x.Time.Year, x.Time.Month })
                .ToDictionary(
                    g => new DateTime(g.Key.Year, g.Key.Month, 1),
                    g => new CoinCandle
                    {
                        Time = new DateTime(g.Key.Year, g.Key.Month, 1),
                        Open = g.First().Open,
                        Close = g.Last().Close,
                        High = g.Max(x => x.High),
                        Low = g.Min(x => x.Low),
                        Volume = g.Sum(x => x.Volume)
                    });

            var result = new List<CoinCandle>();

            for (int i = 11; i >= 0; i--)
            {
                var date = new DateTime(DateTime.UtcNow.AddMonths(-i).Year,
                                         DateTime.UtcNow.AddMonths(-i).Month,
                                         1);

                if (grouped.TryGetValue(date, out var candle))
                    result.Add(candle);
                else
                    result.Add(new CoinCandle
                    {
                        Time = date,
                        Open = 0,
                        Close = 0,
                        High = 0,
                        Low = 0,
                        Volume = 0
                    });
            }

            return result;
        }

        //private List<CoinCandle> BuildMonthly(List<CoinCandle> data)
        //{
        //    return data
        //        .OrderBy(x => x.Time)
        //        .GroupBy(x => new { x.Time.Year, x.Time.Month })
        //        .Select(g => new CoinCandle
        //        {
        //            Time = new DateTime(g.Key.Year, g.Key.Month, 1),
        //            Open = g.First().Open,
        //            Close = g.Last().Close,
        //            High = g.Max(x => x.High),
        //            Low = g.Min(x => x.Low),
        //            Volume = g.Sum(x => x.Volume)
        //        })
        //        .ToList();
        //}

        private DateTime GetWeekStart(DateTime date)
        {
            var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.Date.AddDays(-diff);
        }

        private List<CoinCandle> Parse(JsonElement quotes)
        {
            var list = new List<CoinCandle>();

            foreach (var item in quotes.EnumerateArray())
            {
                var time = item.GetProperty("time_close").GetDateTime();
                var quote = item.GetProperty("quote").GetProperty("USD");

                list.Add(new CoinCandle
                {
                    Time = time,
                    Open = quote.GetProperty("open").GetDecimal(),
                    Close = quote.GetProperty("close").GetDecimal(),
                    High = quote.GetProperty("high").GetDecimal(),
                    Low = quote.GetProperty("low").GetDecimal(),
                    Volume = quote.GetProperty("volume").GetDecimal()
                });
            }

            return list;
        }

        #endregion

        public async Task<EffectivePriceResult> CalculateEffectivePriceAsync(string tokenName, decimal assetQuantity, decimal USDTAmount)
        {
            // Validate input parameters
            if (assetQuantity <= 0) throw new BadRequestException("Token quantity must be greater than zero.", nameof(assetQuantity));
            var priceData = await FetchTokenPriceFromGeckoTerminalAsync(tokenName);


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

    }
}
