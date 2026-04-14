using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._BlockChain;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._Stake.DTOs.Results;
using CoinBank.Services._Stake.DTOs.Settings;
using CoinBank.Services._Withdrawal.DTOs.Results;
using CoinBank.Services._Withdrawal.DTOs.Updates;
using MongoDB.Driver.Linq;
using Utilities.Exceptions.Common;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Withdrawal
{
       
    public class WithdrawalService(
        IWithdrawalRepository _withdrawalRepository,
        IBlockChainService _blockChainService,
        StakeSetting _stakeSetting,
        IStakeRepository _stakeRepository,
        AvailableTokensSettings _availableTokenData) : IWithdrawalService, IScopedDependency
    {

        public async Task SyncStakeWithdrawalsAsync(string stakeReference, bool syncProfit, bool syncAmount)
        {
            var stake = await _stakeRepository.AsQueryable()
                .FirstOrDefaultAsync(x => x.StakeReference == stakeReference)
                ?? throw new NotFoundException("Stake not found!");

            var withdrawalsQuery = _withdrawalRepository.AsQueryable()
                .Where(w =>
                    w.SourceReference == stakeReference &&
                    w.State == WithdrawalState.Success);

            if (syncProfit)
            {
                var totalProfitWithdrawn = await withdrawalsQuery
                    .Where(w => w.Type == WithdrawalType.StakeProfit)
                    .SumAsync(w => (decimal?)w.Amount) ?? 0m;

                stake.TotalProfitWithdrawn = totalProfitWithdrawn;
            }

            if (syncAmount)
            {
                var totalAmountWithdrawn = await withdrawalsQuery
                    .Where(w => w.Type == WithdrawalType.StakeWithdrawal)
                    .SumAsync(w => (decimal?)w.Amount) ?? 0m;

                stake.TotalAmountWithdrawn = totalAmountWithdrawn;
            }

            await _stakeRepository.ReplaceOneAsync(stake);
        }

        //public async Task<WithdrawalResult> WithdrawStakeProfitAsync(WithdrawStakeProfitUpdate update, string publicKey, string evmWalletAddress)
        //{
        //    var stake = await _stakeRepository.AsQueryable().FirstOrDefaultAsync(q => q.StakeReference == update.StakeReference)
        //        ?? throw new NotFoundException("Stake not found!");

        //    if (string.IsNullOrWhiteSpace(publicKey) && publicKey == "guess") throw new BadRequestException("Access denied");

        //    if (stake.UserPublicKey != publicKey)
        //        throw new BadRequestException("Access denied");

        //    if (stake.State != StakeState.Active)
        //        throw new BadRequestException("Stake is not active");

        //    var now = DateTime.UtcNow;

        //    int passedMonths = GetPassedFullMonths(stake.StartMoment, now);

        //    if (passedMonths <= 0)
        //        throw new BadRequestException("Withdraw is not allowed yet");

        //    bool isCompleted = now >= stake.EndMoment;

        //    decimal monthlyPercent;

        //    //var plan = _stakeSetting.Plans
        //    //    .FirstOrDefault(p => p.DurationInMonths == stake.MonthDuration)
        //    //    ?? throw new BadRequestException("Stake plan not found");

        //    monthlyPercent = stake.EachMonthProfitPercent;

        //    var monthlyProfit = stake.TokenAmount * (monthlyPercent / 100m);

        //    var totalProfit = monthlyProfit * passedMonths;

        //    var totalExistingWithdrawn = await _withdrawalRepository.AsQueryable()
        //        .Where(w =>
        //            w.SourceReference == stake.StakeReference &&
        //            w.Type == WithdrawalType.StakeProfit &&
        //            w.State == WithdrawalState.Success)
        //        .SumAsync(w => (decimal?)w.Amount) ?? 0m;

        //    var withdrawable = totalProfit - totalExistingWithdrawn;

        //    if (withdrawable <= 0)
        //        throw new BadRequestException("Nothing to withdraw");

        //    var tokenData = ValidateToken(stake.TokenName);
        //    var amountInWei = _blockChainService.ConvertToWei(withdrawable, tokenData.PriceDecimalPlaces);

        //    var withdrawal = new Withdrawal
        //    {
        //        WithdrawalRerefence = Guid.NewGuid().ToString("N"),
        //        UserPublicKey = stake.UserPublicKey,
        //        WalletAddress = stake.WalletAddress,
        //        Symbol = stake.TokenSymbol,
        //        Network = stake.TokenNetworkName,
        //        Amount = withdrawable,
        //        SourceReference = stake.StakeReference,
        //        Type = WithdrawalType.StakeProfit,
        //        State = WithdrawalState.NotRegistered,
        //        RegisterMoment = null,
        //        RegisterHash = null,
        //        AmountInWei = _blockChainService.ConvertToWei(stake.TokenSymbol,)
        //    };

        //    await _withdrawalRepository.InsertOneAsync(withdrawal);

        //  return ConvertToResult(withdrawal);
        //}

        public async Task<WithdrawalResult> WithdrawStakeAmountAsync(WithdrawStakeAmountUpdate update, string publicKey, string evmWalletAddress)
        {
            var stake = await _stakeRepository.AsQueryable()
                .FirstOrDefaultAsync(q => q.StakeReference == update.StakeReference)
                ?? throw new NotFoundException("Stake not found!");

            if (string.IsNullOrWhiteSpace(publicKey) && publicKey == "guess") throw new BadRequestException("Access denied");

            if (stake.UserPublicKey != publicKey)
                throw new BadRequestException("Access denied");

            if (stake.State != StakeState.Active)
                throw new BadRequestException("Stake is not active");

            var now = DateTime.UtcNow;

            var tokenData = ValidateToken(stake.TokenName);

            var totalWithdrawnAmount = await _withdrawalRepository.AsQueryable()
                .Where(w =>
                    w.SourceReference == stake.StakeReference &&
                    w.Type == WithdrawalType.StakeWithdrawal &&
                    w.State == WithdrawalState.Success)
                .SumAsync(w => (decimal?)w.Amount) ?? 0m;

            var remainingAmount = stake.TokenAmount - totalWithdrawnAmount;

            if (remainingAmount <= 0)
                throw new BadRequestException("No balance left in stake");

            if (update.TokenAmount > remainingAmount)
                throw new BadRequestException("Requested amount exceeds available balance");

            var amountInWei = _blockChainService.ConvertToWei(update.TokenAmount, tokenData.PriceDecimalPlaces);

            var withdrawal = new Withdrawal
            {
                WithdrawalRerefence = Guid.NewGuid().ToString("N"),
                UserPublicKey = stake.UserPublicKey,
                WalletAddress = stake.WalletAddress,
                Symbol = stake.TokenSymbol,
                Network = stake.TokenNetworkName,

                Amount = update.TokenAmount,
                SourceReference = stake.StakeReference,
                Type = WithdrawalType.StakeWithdrawal,
                State = WithdrawalState.NotRegistered,
                RegisterMoment = DateTime.UtcNow,
                AmountInWei = amountInWei.ToString(),
            };

            await _withdrawalRepository.InsertOneAsync(withdrawal);

            return ConvertToResult(withdrawal);

        }


        private int GetPassedFullMonths(DateTime start, DateTime now)
        {
            int months = (now.Year - start.Year) * 12 + (now.Month - start.Month);

            if (now.Day < start.Day)
                months--;

            return Math.Max(0, months);
        }

        private decimal GetEarlyWithdrawPercent(int passedMonths)
        {
            var rule = _stakeSetting.EarlyWithdrawRules
                .FirstOrDefault(r => passedMonths >= r.FromMonth && passedMonths < r.ToMonth);

            if (rule == null)
                throw new BadRequestException("Early withdraw rule not found");

            return rule.MonthlyProfitPercent;
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


        private WithdrawalResult ConvertToResult(Withdrawal w)
        {
            return new WithdrawalResult
            {
                Amount = w.Amount,
                AmountInWei = w.AmountInWei,
                CreatedMoment = w.CreatedMoment,
                ModifiedMoment = w.ModifiedMoment,
                Network = w.Network,
                State = w.State,
                Symbol = w.Symbol,
                Type = w.Type,
                UserPublicKey = w.UserPublicKey,
                WalletAddress = w.WalletAddress,
                WithdrawalRerefence = w.WithdrawalRerefence
            };
        }
      



    }
}
