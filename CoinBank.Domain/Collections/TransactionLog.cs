using Utilities.Attributes;
using Utilities.MongoDatabase.Documents;

namespace CoinBank.Domain.Collections
{


    [MonjoCollectionName("TransactionLogs")]
    public class TransactionLog : BaseDocument
    {
        public string TransactionLogId { get; set; } = Guid.NewGuid().ToString("N");
        public string Reference { get; set; } 
        public string Wallet { get; set; }
        public string Hash { get; set; }
        public string TokenAddress { get; set; }
        public decimal BlockNumber { get; set; }
        public string Data { get; set; }
        public string Network { get; set; } 
        public BlockchainEventType EventType { get; set; } 
        public TransactionStatus Status { get; set; }
    }

    public enum TransactionStatus
    {
        Pending,
        Confirmed,
        Failed
    }

    public enum BlockchainEventType
    {
        PreSaleOrderCreate,
        PreSaleReleaseClaimed, 
        SwapInitiated,
        SwapExecuted,
        SwapFailed,
        SwapCompleted,
        SwapRefunded,
        DepositCreated,
        EarlyWithdrawn,
        ProfitWithdrawn,
        WithdrawnAll,
        TransactionConfirmed, 
        TransactionFailed,
        BlockMined,
        NetworkStatus,
        SwapRefundClaimed
    }
}
