using CoinBank.Domain.Collections;
using CoinBank.Services._Common.DTOs;

namespace CoinBank.Services._Stake.DTOs.Results
{
    public class StakeResult : CommonResult
    {
        public string StakeReference { get; set; }
        public string UserPublicKey { get; set; }
        public string WalletAddress { get; set; }
        public string TokenSymbol { get; set; }
        public string TokenName { get; set; }
        public decimal TokenAmount { get; set; }
        public decimal TokenPrice { get; set; } 
        public decimal EachMonthProfit { get; set; }
        public int MonthDuration { get; set; }
        public DateTime StartMoment { get; set; }
        public DateTime EndMoment { get; set; }
        public decimal TotalProfitWithdrawn { get; set; }
        public decimal TotalAmountWithdrawn { get; set; }
        public StakeState State { get; set; }
        public decimal EachMonthProfitPercent { get; set; }

    }
}
