using CoinBank.Domain.Collections;

namespace CoinBank.Services._PreSaleOrder.DTOs.Results
{
    public class PreSaleOrderWalletStatsResult
    {
        public string Name { get; set; }
        public string Symbol { get; set; }
        public string LogoUrl { get; set; }

        public decimal TokenAmount { get; set; }
        public decimal TokenPrice { get; set; }
        public decimal TotalPrice { get; set; }

        public int OrderCount { get; set; }
        public List<PreSaleReleaseStep> ReleaseSchedule { get; set; }

    }
}
