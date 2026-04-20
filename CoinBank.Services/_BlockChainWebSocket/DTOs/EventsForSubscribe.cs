using Nethereum.ABI.FunctionEncoding.Attributes;
using System.Numerics;

namespace CoinBank.Services._BlockChainWebSocket.DTOs
{

    [Event("InsuranceRegistered")]
    public class InsuranceRegisteredEventDTO : IEventDTO
    {
        [Parameter("bytes32", "insuranceId", 1, false)]
        public byte[] InsuranceId { get; set; }

        [Parameter("address", "user", 2, false)]
        public string User { get; set; }

        [Parameter("address", "insuredToken", 3, false)]
        public string InsuredToken { get; set; }

        [Parameter("uint256", "coverageAmount", 4, false)]
        public BigInteger CoverageAmount { get; set; }
    }

    [Event("InsuranceFinalized")]
    public class InsuranceFinalizedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "insuranceId", 1, false)]
        public byte[] InsuranceId { get; set; }

        [Parameter("address", "user", 2, false)]
        public string User { get; set; }

        [Parameter("uint256", "settlementAmount", 3, false)]
        public BigInteger SettlementAmount { get; set; }

        [Parameter("uint256", "finalPrice", 4, false)]
        public BigInteger FinalPrice { get; set; }

        [Parameter("address", "payoutToken", 5, false)]
        public string PayoutToken { get; set; }

        [Parameter("uint256", "payoutAmount", 6, false)]
        public BigInteger PayoutAmount { get; set; }
    }

    [Event("InsuranceCancelled")]
    public class InsuranceCancelledEventDTO : IEventDTO
    {
        [Parameter("bytes32", "insuranceId", 1, false)]
        public byte[] InsuranceId { get; set; }

        [Parameter("address", "user", 2, false)]
        public string User { get; set; }
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
