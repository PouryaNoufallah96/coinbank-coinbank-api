using Utilities.Attributes;
using Utilities.MongoDatabase.Documents;

namespace CoinBank.Domain.Collections
{

    [MonjoCollectionName("PreSaleOrders")]
    public class PreSaleOrder : BaseDocument
    {
        public string PreSaleOrderReference { get; set; }
        public string PreSaleReference { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public string LogoUrl { get; set; }

        public string UserPublicKey { get; set; }
        public string WalletAddress { get; set; }

        public decimal TokenPrice { get; set; }
        public decimal TokenAmount { get; set; }
        public decimal TotalValue { get; set; }

        public string RegisterHash { get; set; }
        public DateTime? RegisterMoment { get; set; } = null;
        public PreSaleOrderState State { get; set; } = PreSaleOrderState.NotRegistered;
        public string PaidToken { get; set; }  

        public List<PreSaleReleaseStep> ReleaseSchedule { get; set; }
    }

    public enum PreSaleOrderState { NotRegistered, InProgress, Completed, Expired, Failed };

}
