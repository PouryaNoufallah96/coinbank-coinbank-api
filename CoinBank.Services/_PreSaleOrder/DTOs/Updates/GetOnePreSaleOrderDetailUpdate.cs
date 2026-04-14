using Utilities.Attributes;

namespace CoinBank.Services._PreSaleOrder.DTOs.Updates
{
    public class GetOnePreSaleOrderDetailUpdate
    {
       [StringInputValidation] public string PreSaleOrderReference { get; set; }
    }
}
