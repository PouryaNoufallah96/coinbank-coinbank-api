using CoinBank.Domain.Collections;
using CoinBank.Services._Common.DTOs;

namespace CoinBank.Services._Swap.DTOs.Results
{
    public class SwapListResult
    {
        public List<SwapResult> Data { get; set; } = [];
        public int PageCount { get; set; } = 0;
        public int TotalCount { get; set; } = 0;
    }

    public class SwapResult : CommonResult
    {
        public string SwapReference { get; set; }
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

        public SwapState State { get; set; } 
        public List<SwapTransaction> Transactions { get; set; }
    }


}
