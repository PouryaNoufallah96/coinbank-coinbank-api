using CoinBank.Domain.Collections;
using CoinBank.Services._Common.DTOs;

namespace CoinBank.Services._PreSaleOrder.DTOs.Results
{
    public class PreSaleOrderResult : CommonResult
    {
        public string PreSaleReference { get; set; }
        public string PreSaleOrderReference { get; set; } 
        public string Name { get; set; }
        public string Symbol { get; set; }
        public string LogoUrl { get; set; }
        public string WalletAddress { get; set; }
        public decimal TokenPreSalePrice { get; set; }
        public decimal ReceivingTokenAmount { get; set; }
        public string ReceivingTokenAmountInWei { get; set; }
        public string PaymentToken { get; set; }
        public decimal PaymentTokenAmount { get; set; }
        public string PaymentTokenAmountInWei { get; set; }
        public PreSaleOrderState State { get; set; }
        public List<PreSaleOrderReleaseStep> ReleaseSchedule { get; set; }
        public string Signature { get; set; }
        public DateTime SignatureExpire { get; set; }
    }
}
