using CoinBank.Domain.Collections;
using System.Numerics;

namespace CoinBank.Services._Transaction.DTOs.Updates
{
    public class SwapInitiatedLog
    {
        public string Hash { get; set; }
        public string Address { get; set; }
        public BigInteger BlockNumber { get; set; }

        public string Buyer { get; set; }
        public string SwapId { get; set; }
        public string SourceTokenAddress { get; set; }
        public string DestinationTokenAddress { get; set; }
        public uint DesEid { get; set; }
        public BigInteger SourceTokenAmount { get; set; }
        public string Network { get; set; } 
        public BigInteger DestinationTokenAmount { get; set; }  
        public string DestinationWallet { get; set; }
        public string Fee { get; set; }
        public BlockchainEventType EventType { get; set; }
    }

    public class SwapInitiatedLogData
    {
        public string SwapId { get; set; }
        public string SourceTokenAddress { get; set; }
        public string DestinationTokenAddress { get; set; }
        public string DesEid { get; set; }
        public string SourceTokenAmount { get; set; }
        public string DestinationTokenAmount { get; set; }
        public string DestinationWallet { get; set; }
        public string Fee { get; set; }

    }



    public class SwapExecutedLog
    {
        public string Hash { get; set; }
        public string Address { get; set; }
        public BigInteger BlockNumber { get; set; }
        public string Network { get; set; }

        public string SwapId { get; set; }
        public string DestinationTokenAddress { get; set; }
        public BigInteger DestinationTokenAmount { get; set; }
        public string DestinationWallet { get; set; }
        public BlockchainEventType EventType { get; set; }
    }

    public class SwapExecutedLogData
    {
        public string SwapId { get; set; }
        public string DestinationTokenAddress { get; set; }
        public string DestinationTokenAmount { get; set; }
        public string DestinationWallet { get; set; }
    }




    public class SwapFailedLog
    {
        public string Hash { get; set; }
        public string Address { get; set; }
        public BigInteger BlockNumber { get; set; }
        public string Network { get; set; }

        public string SwapId { get; set; }
        public string DestinationTokenAddress { get; set; }
        public BigInteger DestinationTokenAmount { get; set; }
        public string DestinationWallet { get; set; }
        public BlockchainEventType EventType { get; set; }
    }

    public class SwapFailedLogData
    {
        public string SwapId { get; set; }
        public string DestinationTokenAddress { get; set; }
        public string DestinationTokenAmount { get; set; }
        public string DestinationWallet { get; set; }

    }

    public class SwapCompletedLog
    {
        public string Hash { get; set; }
        public string Address { get; set; }
        public BigInteger BlockNumber { get; set; }
        public string SwapId { get; set; }
        public BlockchainEventType EventType { get; set; }
        public string Network { get; set; }
    }

    public class SwapCompletedLogData
    {
        public string SwapId { get; set; }
    }

    public class SwapRefundedLog
    {
        public string Hash { get; set; }
        public string Address { get; set; }
        public BigInteger BlockNumber { get; set; }
        public string SwapId { get; set; }
        public string Token { get; set; }
        public BigInteger Amount { get; set; }
        public string User { get; set; }
        public BlockchainEventType EventType { get; set; }
        public string Network { get; set; }
    }

    public class SwapRefundedLogData
    {
        public string SwapId { get; set; }
        public string Token { get; set; }
        public string Amount { get; set; }
        public string User { get; set; }
    }

}
