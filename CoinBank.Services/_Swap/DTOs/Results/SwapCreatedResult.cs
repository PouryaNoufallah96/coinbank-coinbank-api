using CoinBank.Domain.Collections;
using CoinBank.Services._Common.DTOs;
using System.Reactive;

namespace CoinBank.Services._Swap.DTOs.Results
{
    public class SwapCreatedResult : CommonResult
    {
        public string SwapReference { get; set; }
        public string WalletAddress { get; set; }

        public string SourceNetwork { get; set; }
        public string SourceSymbol { get; set; }
        public string SourceTokenAddress { get; set; }
        public decimal SourceTokenPrice { get; set; }
        public decimal SourceAmount { get; set; }
        public string SourceAmountInWei { get; set; }
        public string SourceWallet { get; set; }
        public uint DstEid { get; set; }  
        public uint SrcEid { get; set; }   


        public string DestinationNetwork { get; set; }
        public string DestinationSymbol { get; set; }
        public decimal DestinationTokenPrice { get; set; }
        public decimal DestinationMinAmount { get; set; }
        public string DestinationMinAmountInWei { get; set; } 
        public string DestinationTokenAddress { get; set; }
        public string DestinationWallet { get; set; }

        public string FeeToken { get; set; }
        public decimal Fee { get; set; }
        public string EstimatedReturnFee { get; set; } 
        public SwapState State { get; set; }
        public List<SwapTransaction> Transactions { get; set; } = [];
    }
}
