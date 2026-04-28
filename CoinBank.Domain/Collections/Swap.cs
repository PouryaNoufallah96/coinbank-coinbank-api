using Utilities.Attributes;
using Utilities.MongoDatabase.Documents;

namespace CoinBank.Domain.Collections
{

    [MonjoCollectionName("Swaps")]
    public class Swap : BaseDocument
    {
        public string SwapReference { get; set; }
        public string UserPublicKey { get; set; }
        public string WalletAddress { get; set; }

        public string SourceNetwork { get; set; }
        public string SourceSymbol { get; set; }
        public decimal SourceTokenPrice { get; set; }
        public decimal SourceAmount { get; set; }
        public string SourceAmountInWei { get; set; }
        public string SourceWallet { get; set; }

        public string DestinationNetwork { get; set; }
        public string DestinationSymbol { get; set; }
        public decimal DestinationTokenPrice { get; set; }
        public decimal DestinationAmount { get; set; }
        public string DestinationAmountInWei { get; set; }
        public string DestinationWallet { get; set; }

        public string FeeToken { get; set; }
        public decimal Fee { get; set; }
        public string RegisterHash { get; set; }
        public DateTime? RegisterMoment { get; set; } = null;

        public SwapState State { get; set; } = SwapState.NotRegistered;
        public List<SwapTransaction> Transactions { get; set; }
    }

    public enum SwapState { NotRegistered, Pending, Compelete, Canceled }

    public class SwapTransaction
    {

        public DateTime CreateMoment { get; set; }
        public string Hash { get; set; }
        public string Symbol { get; set; }
        public string Network { get; set; }
        public decimal Amount { get; set; }
        public SwapTransactionType Type { get; set; }
    }

    public enum SwapTransactionType { Init, Execute }

}
