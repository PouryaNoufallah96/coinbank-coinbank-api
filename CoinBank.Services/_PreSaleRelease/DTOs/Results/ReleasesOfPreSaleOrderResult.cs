using CoinBank.Services._Common.DTOs;

namespace CoinBank.Services._PreSaleRelease.DTOs.Results
{
    public class ReleasesOfPreSaleOrderResult : CommonResult
    {
        public string PreSaleReleaseReference { get; set; } 
        public string PreSaleOrderReference { get; set; }
        public string WalletAddress { get; set; }
        public decimal ReleaseAmount { get; set; }
        public string TransactionHash { get; set; }
    }
}
