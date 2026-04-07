using CoinBank.Domain.Collections;
using CoinBank.Services._Common.DTOs;

namespace CoinBank.Services._PreSaleOrder.DTOs.Results
{
    public class PreSaleOrderResult : CommonResult
    {
        public string PreSaleOrderReference { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public string LogoUrl { get; set; }
        public string WalletAddress { get; set; }
        public decimal TokenPrice { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal TokenAmount { get; set; } 
        public decimal AvailableForWithdrawalAmount { get; set; } 
        public List<PreSaleReleaseStep> ReleaseSchedule { get; set; }
    }
}
