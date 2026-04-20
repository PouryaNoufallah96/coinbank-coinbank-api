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

        public decimal TokenPreSalePrice { get; set; }
        public decimal ReceivingTokenAmount { get; set; }
        public string ReceivingTokenAmountInWei { get; set; }
        public string PaymentToken { get; set; }
        public decimal PaymentTokenAmount { get; set; }
        public string PaymentTokenAmountInWei { get; set; }


        public string RegisterHash { get; set; }
        public DateTime? RegisterMoment { get; set; } = null;
        public PreSaleOrderState State { get; set; } = PreSaleOrderState.NotRegistered;

        public List<PreSaleOrderReleaseStep> ReleaseSchedule { get; set; }

        public string Signature { get; set; }
        public DateTime SignatureExpire { get; set; }
    }


    public class PreSaleOrderReleaseStep
    {
        public DateTime ReleaseDate { get; set; }
        public decimal Percentage { get; set; }
        public DateTime? RegisterMoment { get; set; } = null;
        public string RegisterHash { get; set; } = null;
    }



    public enum PreSaleOrderState { NotRegistered, InProgress, Completed, Expired, Failed };

}
