using CoinBank.Domain.Collections;

namespace CoinBank.Services._PreSaleOrder.DTOs.Results
{
    public class PreSaleOrderWalletStatsResult
    {
        public string PreSaleOrderReference { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public string LogoUrl { get; set; }
         
        public decimal ReceivingTokenAmount { get; set; }
        public decimal TokenPreSalePrice { get; set; }
        public decimal TotalPaymentToken { get; set; }

        public int OrderCount { get; set; }
        public List<PreSaleOrderReleaseStep> ReleaseSchedule { get; set; }

    }
}
