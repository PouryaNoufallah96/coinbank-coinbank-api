using Nethereum.ABI.FunctionEncoding.Attributes;
using System.Numerics;

namespace CoinBank.Services._BlockChainWebSocket.DTOs
{
    [Event("SwapInitiated")]
    public class SwapInitiatedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "swapId", 1, false)]
        public byte[] SwapId { get; set; }

        [Parameter("uint32", "dstEid", 2, false)]
        public uint DstEid { get; set; }

        [Parameter("address", "tokenIn", 3, false)]
        public string TokenIn { get; set; }

        [Parameter("address", "tokenOut", 4, false)]
        public string TokenOut { get; set; }

        [Parameter("uint256", "amountIn", 5, false)]
        public BigInteger AmountIn { get; set; }

        [Parameter("uint256", "amountOut", 6, false)]
        public BigInteger AmountOut { get; set; }

        [Parameter("address", "receiver", 7, false)]
        public string Receiver { get; set; }

        [Parameter("uint256", "fee", 8, false)]
        public BigInteger Fee { get; set; }
    }

    [Event("SwapExecuted")]
    public class SwapExecutedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "swapId", 1, false)]
        public byte[] SwapId { get; set; }

        [Parameter("address", "tokenOut", 2, false)]
        public string TokenOut { get; set; }

        [Parameter("uint256", "amountOut", 3, false)]
        public BigInteger AmountOut { get; set; }

        [Parameter("address", "receiver", 4, false)]
        public string Receiver { get; set; }
    }

    [Event("SwapFailed")]
    public class SwapFailedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "swapId", 1, false)]
        public byte[] SwapId { get; set; }

        [Parameter("address", "tokenOut", 2, false)]
        public string TokenOut { get; set; }

        [Parameter("uint256", "amountOut", 3, false)]
        public BigInteger AmountOut { get; set; }

        [Parameter("address", "receiver", 4, false)]
        public string Receiver { get; set; }
    }

    [Event("SwapCompleted")]
    public class SwapCompletedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "swapId", 1, false)]
        public byte[] SwapId { get; set; }
    }

    [Event("SwapRefunded")]
    public class SwapRefundedEventDTO : IEventDTO
    {
        [Parameter("bytes32", "swapId", 1, false)]
        public byte[] SwapId { get; set; }

        [Parameter("address", "token", 2, false)]
        public string Token { get; set; }

        [Parameter("uint256", "amount", 3, false)]
        public BigInteger Amount { get; set; }

        [Parameter("address", "user", 4, false)]
        public string User { get; set; }
    }

}
