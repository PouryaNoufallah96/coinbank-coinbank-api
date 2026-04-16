using CoinBank.Services._Withdrawal.DTOs.Results;

namespace CoinBank.Services._Stake.DTOs.Results
{
    public class StakeDetailResult : StakeResult
    {
        public decimal AvailableProfitForWithdraw { get; set; }
        public List<WithdrawalResult> Withrawals { get; set; } 
    }
}
