using CoinBank.Domain.Collections;
using System.Numerics;

namespace CoinBank.Services._Transaction.DTOs.Updates
{
    public class PreSaleReleaseClaimedLog
    {
        public string Hash { get; set; }
        public string Address { get; set; }
        public BigInteger BlockNumber { get; set; }

        public string Buyer { get; set; }
        public string SaleId { get; set; }
        public string OrderId { get; set; }

        public string AmountClaimed { get; set; }

        public BlockchainEventType EventType { get; set; }
    }


    public class PreSaleReleaseClaimedData
    {
        public string SaleId { get; set; }
        public string OrderId { get; set; }
        public string Buyer { get; set; }
        public string AmountClaimed { get; set; }
    }





    public class PreSaleOrderCreateLog
    {
        public string Hash { get; set; }
        public string Address { get; set; }
        public BigInteger BlockNumber { get; set; }

        public string Buyer { get; set; }
        public string SaleId { get; set; }
        public string OrderId { get; set; }

        public string AmountPurchased { get; set; }
        public string AmountPaid { get; set; }

        public BlockchainEventType EventType { get; set; }

    }


    public class PreSaleOrderCreateData
    {
        public string SaleId { get; set; }
        public string OrderId { get; set; }
        public string Buyer { get; set; }
        public string AmountPurchased { get; set; }
        public string AmountPaid { get; set; }
    }

    


}
