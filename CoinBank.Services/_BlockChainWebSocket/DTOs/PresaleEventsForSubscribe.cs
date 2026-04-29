using Nethereum.ABI.FunctionEncoding.Attributes;
using System.Numerics;

namespace CoinBank.Services._BlockChainWebSocket.DTOs
{

    [Event("Claimed")]
    public class ClaimedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "saleId", 1, false)]
        public byte[] SaleId { get; set; }

        [Parameter("bytes32", "orderId", 2, false)]
        public byte[] OrderId { get; set; }

        [Parameter("address", "buyer", 3, false)]
        public string Buyer { get; set; }

        [Parameter("uint256", "amountClaimed", 4, false)]
        public BigInteger AmountClaimed { get; set; }
    }

    [Event("PresaleConfigured")]
    public class PresaleConfiguredEventDTO : IEventDTO
    {
        [Parameter("bytes32", "saleId", 1, false)]
        public byte[] SaleId { get; set; }

        [Parameter("address", "token", 2, false)]
        public string Token { get; set; }

        [Parameter("uint256", "totalAllocation", 3, false)]
        public BigInteger TotalAllocation { get; set; }

        [Parameter("uint256", "maxPerWallet", 4, false)]
        public BigInteger MaxPerWallet { get; set; }

        [Parameter("uint64", "start", 5, false)]
        public ulong Start { get; set; }

        [Parameter("uint64", "end", 6, false)]
        public ulong End { get; set; }
    }

    [Event("Purchased")]
    public class PurchasedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "saleId", 1, false)]
        public byte[] SaleId { get; set; }

        [Parameter("bytes32", "orderId", 2, false)]
        public byte[] OrderId { get; set; }

        [Parameter("address", "buyer", 3, false)]
        public string Buyer { get; set; }

        [Parameter("uint256", "amountPurchased", 4, false)]
        public BigInteger AmountPurchased { get; set; }

        [Parameter("uint256", "amountPaid", 5, false)]
        public BigInteger AmountPaid { get; set; }
    }

    [Event("Transfer")]
    public class TransferEventDTO : IEventDTO
    {
        [Parameter("address", "_from", 1, true)]
        public string From { get; set; }

        [Parameter("address", "_to", 2, true)]
        public string To { get; set; }

        [Parameter("uint256", "_value", 3, false)]
        public BigInteger Value { get; set; }
    }

}
