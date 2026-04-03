using Utilities.Attributes;
using Utilities.MongoDatabase.Documents;

namespace CoinBank.Domain.Collections
{

    [MonjoCollectionName("PreSales")]
    public class PreSale : BaseDocument  
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

        public string RegisterHash { get; set; }
        public DateTime RegisterMoment { get; set; }
    }

    public class PreSaleReleaseStep
    {
        public DateTime ReleaseDate { get; set; }
        public decimal Percentage { get; set; }
    }

    public enum PreSaleState { Pending, Active, Expired, Failed };
}
