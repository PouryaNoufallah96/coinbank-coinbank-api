using Utilities.Attributes;
using Utilities.MongoDatabase.Documents;

namespace CoinBank.Domain.Collections
{

    [MonjoCollectionName("Stakes")]
    public class Stake : BaseDocument
    {
        public string StakeReference { get; set; }
        public string UserPublicKey { get; set; }
        public string WalletAddress { get; set; }

        public string TokenSymbol { get; set; }
        public decimal TokenAmount { get; set; }
        public decimal EachMonthProfit { get; set; } 

        public StakeType StakeType { get; set; }
        public DateTime StartMoment { get; set; } 
        public int MonthDuration { get; set; }
        public DateTime EndMoment { get; set; }
        public decimal EstimatedTotalProfitInToken { get; set; }

    }

    public enum StakeType { OneYear, TwoYear };
}
