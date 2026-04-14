using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._BlockChain;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._Price;
using CoinBank.Services._Stake.DTOs.Results;
using CoinBank.Services._Stake.DTOs.Settings;
using CoinBank.Services._Stake.DTOs.Updates;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Utilities.Exceptions.Common;
using Utilities.Utilities;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Stake
{
    public class StakeService(IStakeRepository _stakeRepository,
        StakeSetting _stakeSetting,
        AvailableTokensSettings _availableTokenData,
        IPriceService _priceService,
        IWithdrawalRepository _withdrawalRepository,
        IBlockChainService _blockChainService)
        : IStakeService, IScopedDependency
    {

        /// <summary>
        /// use for create a new stake
        /// </summary>
        /// <param name="update"></param>
        /// <param name="publicKey"></param>
        /// <param name="evmWalletAddress"></param>
        /// <returns></returns>
        /// <exception cref="BadRequestException"></exception>
        public async Task<StakeResult> CreateStakeAsync(CreateStakeUpdate update, string publicKey, string evmWalletAddress)
        {
            if (update == null)
                throw new BadRequestException("Request body is required");



            var symbol = update.Symbol.Trim().ToUpper();
            var tokenData = ValidateToken(symbol);

            if (!_stakeSetting.AllowedTokensSymbol
                .Any(s => s.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BadRequestException($"Token '{update.Symbol}' is not allowed for staking");
            }

            var plan = _stakeSetting.Plans
                .FirstOrDefault(p => p.DurationInMonths == update.Duration)
                ?? throw new BadRequestException("Invalid staking duration. Allowed durations are based on configured plans 12 and 24 month");


            var tokenBalance = await _blockChainService.GetWalletAddressSingleTokenBalanceAsync(evmWalletAddress, symbol);
            if (tokenBalance < update.Amount)
                throw new BadRequestException($"Insufficient {symbol} balance!");


            var eachMonthProfit = update.Amount * (plan.MonthlyProfitPercent / 100);

            var start = DateTime.UtcNow;
            var end = start.AddMonths(update.Duration);

            var reference = Guid.NewGuid().ToString("N");
            var tokenPrice = await _priceService.GetOneTokenPriceForInternalUsage(symbol);

            var stake = new Stake
            {
                StakeReference = reference,
                UserPublicKey = publicKey,
                WalletAddress = evmWalletAddress,
                TokenSymbol = symbol,
                TokenAmount = update.Amount,
                TokenPrice = tokenPrice,
                TokenName = tokenData.PoolName,
                EachMonthProfitPercent = plan.MonthlyProfitPercent,
                EachMonthProfit = eachMonthProfit,
                MonthDuration = update.Duration,
                StartMoment = start,
                EndMoment = end,
                TokenNetworkName = tokenData.Network,
                TotalProfitWithdrawn = 0,
                State = StakeState.NotRegistered
            };

            await _stakeRepository.InsertOneAsync(stake);
            return ConvertToResult(stake);
        }


        /// <summary>
        /// use for get user stake history
        /// </summary>
        /// <param name="update"></param>
        /// <param name="publicKey"></param>
        /// <param name="evmWalletAddress"></param>
        /// <returns></returns>
        public async Task<StakeListResult> GetStakeHistoryAsync(StakeHistoryUpdate update, string publicKey, string evmWalletAddress)
        {
            var query = _stakeRepository.AsQueryable().Where(q => q.State != StakeState.NotRegistered);

            if (update.Symbol != null && update.Symbol.HasValue())
            {
                query = query.Where(q => q.TokenSymbol == update.Symbol.ToUpper());
            }

            if (string.IsNullOrWhiteSpace(publicKey) || publicKey == "guess")
            {
                query = query.Where(x =>
                    x.WalletAddress == evmWalletAddress);
            }
            else
            {
                query = query.Where(x =>
                    x.UserPublicKey == publicKey);
            }

            var totalCount = await query.CountAsync();

            var page = update.Pagination?.Page ?? 1;
            var size = update.Pagination?.Size ?? 25;

            var data = await query
                .OrderByDescending(x => x.CreatedMoment)
                .Skip((page - 1) * size)
                .Take(size)
                .ToListAsync();

            var result = new StakeListResult
            {
                TotalCount = totalCount,
                PageCount = (int)Math.Ceiling((double)totalCount / size),
                Data = data.Select(ConvertToResult).ToList()
            };

            return result;
        }


        /// <summary>
        /// use for get wallet stats for stake section
        /// </summary>
        /// <param name="update"></param>
        /// <param name="publicKey"></param>
        /// <param name="evmWalletAddress"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<List<StakeWalletStatsResult>> GetWalletStatsAsync(GetStakeWalletStatsUpdate update, string publicKey, string evmWalletAddress)
        {
            var query = _stakeRepository.AsQueryable().Where(q => q.State != StakeState.NotRegistered);

            if (string.IsNullOrWhiteSpace(publicKey) || publicKey == "guess")
            {
                query = query.Where(x =>
                    x.WalletAddress == evmWalletAddress);
            }
            else
            {
                query = query.Where(x =>
                    x.UserPublicKey == publicKey);
            }

            if (update.ShouldGrouped)
            {
                var grouped = await query
                    .GroupBy(x => x.TokenSymbol)
                    .Select(g => new StakeWalletStatsResult
                    {
                        Symbol = g.Key,
                        Name = g.First().TokenName,

                        TokenAmount = g.Sum(x => x.TokenAmount),
                        StakeCount = g.Count()
                    })
                    .ToListAsync();

                return grouped;
            }

            var list = await query
                .Select(x => new StakeWalletStatsResult
                {
                    Symbol = x.TokenSymbol,
                    Name = x.TokenName,

                    TokenAmount = x.TokenAmount,
                    StakeCount = 1
                })
                .ToListAsync();

            return list;
        }

      

     


        /// <summary>
        /// convertor
        /// </summary>
        /// <param name="stake"></param>
        /// <returns></returns>
        public StakeResult ConvertToResult(Stake stake)
        {

            return new StakeResult
            {
                StakeReference = stake.StakeReference,
                UserPublicKey = stake.UserPublicKey,
                WalletAddress = stake.WalletAddress,
                TokenSymbol = stake.TokenSymbol,
                TokenName = stake.TokenName,
                TokenAmount = stake.TokenAmount,
                TokenPrice = stake.TokenPrice,
                MonthDuration = stake.MonthDuration,
                StartMoment = stake.StartMoment,
                EndMoment = stake.EndMoment,
                TotalProfitWithdrawn = stake.TotalProfitWithdrawn,
                TotalAmountWithdrawn = stake.TotalAmountWithdrawn,
                State = stake.State,
                EachMonthProfit = stake.EachMonthProfit,
                EachMonthProfitPercent = stake.EachMonthProfitPercent,
                CreatedMoment = stake.CreatedMoment,
                ModifiedMoment = stake.ModifiedMoment,
            };

        }


        /// <summary>
        /// use for check token existing and return token data
        /// </summary>
        /// <param name="tokenName"></param>
        /// <returns></returns>
        /// <exception cref="BadRequestException"></exception>
        private AvailableTokenData ValidateToken(string tokenName)
        {
            var tokenData = _availableTokenData.FirstOrDefault(q => q.Name.Equals(tokenName, StringComparison.CurrentCultureIgnoreCase))
                ?? throw new BadRequestException($"Unsupported token name! {tokenName}");
            return tokenData;
        }

    }
}
