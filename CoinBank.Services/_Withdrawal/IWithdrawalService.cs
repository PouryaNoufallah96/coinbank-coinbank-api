using CoinBank.Services._Withdrawal.DTOs.Results;
using CoinBank.Services._Withdrawal.DTOs.Updates;

namespace CoinBank.Services._Withdrawal
{
    public interface IWithdrawalService
    {
        Task SyncStakeWithdrawalsAsync(string stakeReference, bool syncProfit, bool syncAmount);
        //Task<WithdrawalResult> WithdrawStakeProfitAsync(WithdrawStakeProfitUpdate update, string publicKey, string evmWalletAddress);
        //Task<WithdrawalResult> WithdrawStakeAmountAsync(WithdrawStakeAmountUpdate update, string publicKey, string evmWalletAddress);

    }
}
