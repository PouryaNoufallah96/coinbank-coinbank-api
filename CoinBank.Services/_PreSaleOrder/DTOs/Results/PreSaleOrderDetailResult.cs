using CoinBank.Domain.Collections;
using CoinBank.Services._Common.DTOs;
using CoinBank.Services._PreSaleRelease.DTOs.Results;

namespace CoinBank.Services._PreSaleOrder.DTOs.Results
{
    public class PreSaleOrderDetailResult : CommonResult
    {
        public string PreSaleOrderReference { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public string LogoUrl { get; set; }
        public string WalletAddress { get; set; }
        public decimal TokenPrice { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal TokenAmount { get; set; }

        public PreSaleOrderState State { get; set; }
        public List<PreSaleReleaseStep> ReleaseSchedule { get; set; }
        public List<ReleasesOfPreSaleOrderResult> ReleaseTransactions { get; set; }
        public decimal TotalReleasedTokenAmount { get; set; } 
        public decimal RemainReleaseTokenAmount { get; set; }
    }
}
