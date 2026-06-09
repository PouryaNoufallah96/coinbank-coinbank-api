using System.Numerics;
using Utilities.Attributes;

namespace CoinBank.Services._BlockChain.DTOs.Updates
{
    public class GetSwapEstimatedFeeUpdate
    {
        [StringInputValidation] public string SwapReference { get; set; }
        public uint DstEid { get; set; }
        [StringInputValidation] public string SourceNetwork { get; set; }
        [StringInputValidation] public string SourceTokenAddress { get; set; } //symbol
        public BigInteger SourceAmoutInWei { get; set; }
        [StringInputValidation] public string DestinationNetwork { get; set; }
        [StringInputValidation] public string DestinationTokenAddress { get; set; } //symbol 
        [StringInputValidation] public string DestinationWallet { get; set; }

        public BigInteger EstimatedReturnFee { get; set; } 
        public List<string> Paths { get; set; } 
    }


    public class SwapGetOutputAmount
    {
        [StringInputValidation] public string SourceTokenAddress { get; set; }
        public uint SrcEid { get; set; } 

        public BigInteger SourceAmountInWei { get; set; }
        [StringInputValidation] public string DestinationTokenAddress { get; set; }
        public uint DstEid { get; set; }

        public List<string> Paths { get; set; }

    }

}
