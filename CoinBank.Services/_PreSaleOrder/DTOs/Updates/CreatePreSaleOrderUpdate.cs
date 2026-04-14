using Utilities.Attributes;

namespace CoinBank.Services._PreSaleOrder.DTOs.Updates
{
    public class CreatePreSaleOrderUpdate
    {
        [StringInputValidation] public string PreSaleReference { get; set; }
        [NumericInputValidation(isRequired: true, mustBeNonZero: true)] public decimal TokenAmount { get; set; }
    }
}
