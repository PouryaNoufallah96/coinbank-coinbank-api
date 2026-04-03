using CoinBank.Domain.Collections;
using CoinBank.Services._Common.DTOs;

namespace CoinBank.Services._PreSale.DTOs.Results
{
    public class PreSaleResult : CommonResult
    {
        public string PreSaleReference { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public string LogoUrl { get; set; }
        public string Description { get; set; }

        public decimal TotalSupply { get; set; } // amount in token like 500000
        public decimal MaxPerOrder { get; set; } //  amount in token like 5000
        public decimal MinPerOrder { get; set; }  //  amount in token like 50
        public decimal Price { get; set; }

        public DateTime StartSellingAt { get; set; }
        public DateTime EndSellingAt { get; set; }
        public List<PreSaleReleaseStep> ReleaseSchedule { get; set; }
        public PreSaleState State { get; set; }

    }
}
