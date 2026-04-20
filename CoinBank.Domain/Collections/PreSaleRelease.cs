//using Utilities.Attributes;
//using Utilities.MongoDatabase.Documents;

//namespace CoinBank.Domain.Collections
//{
//    [MonjoCollectionName("PreSaleReleases")]
//    public class PreSaleRelease : BaseDocument
//    {
//        public string PreSaleReleaseReference { get; set; } = Guid.NewGuid().ToString("N");
//        public string PreSaleOrderReference { get; set; }
//        public string WalletAddress { get; set; }
//        public string TokenSymbol { get; set; }
//        public decimal ReleasePercentage { get; set; } 
//        public decimal ReleaseAmount { get; set; } 
//        public DateTime ScheduledAt { get; set; }
//        public string TransactionHash { get; set; }
//    }
//}
