using Utilities.Attributes;
using Utilities.MongoDatabase.Documents;

namespace CoinBank.Domain.Collections
{
    [MonjoCollectionName("Withdrawals")]
    public class Withdrawal : BaseDocument
    {
        public string WithdrawalRerefence { get; set; }
        public string UserPublicKey { get; set; }
        public string WalletAddress { get; set; }
        public string Symbol { get; set; }
        public string Network { get; set; }
        public decimal Amount { get; set; }
        //public string DestinationWallet { get; set; } // cause amount return to wallet owner == WalletAdress
        public WithdrawalType Type { get; set; }
        public WithdrawalState State { get; set; }
        public string RegisterHash { get; set; }
        public DateTime? RegisterMoment { get; set; } = null;
    }

    public enum WithdrawalType { PreSale, StakeProof }
    public enum WithdrawalState { Pending, Success, Failed }

}
