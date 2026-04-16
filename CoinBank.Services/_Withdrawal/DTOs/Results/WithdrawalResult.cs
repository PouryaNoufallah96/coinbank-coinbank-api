using CoinBank.Domain.Collections;
using CoinBank.Services._Common.DTOs;

namespace CoinBank.Services._Withdrawal.DTOs.Results
{
    public class WithdrawalResult : CommonResult
    {
        public string WithdrawalRerefence { get; set; }
        public string UserPublicKey { get; set; }
        public string WalletAddress { get; set; }
        public string Symbol { get; set; }
        public string Network { get; set; }
        public decimal Amount { get; set; }
        public decimal ProfitAmount { get; set; }
        public decimal Cost { get; set; }
        public decimal FinalAmount { get; set; }
        public string FinalAmountInWei { get; set; } 
        public WithdrawalType Type { get; set; }
        public WithdrawalState State { get; set; }
    }
}
