using Utilities.Attributes;

namespace CoinBank.Services._Swap.DTOs.Updates
{
    public class CreateSwapUpdate
    {
        [StringInputValidation(maxLength: 50)] public string SourceSymbol { get; set; }
        [StringInputValidation(maxLength: 50)] public string SourceNetwork { get; set; }
        [NumericInputValidation(isRequired: true, mustBeNonZero: true)] public decimal SourceTokenAmount { get; set; }

        [StringInputValidation(maxLength: 50)] public string DestinationToken { get; set; }
        [StringInputValidation(maxLength: 50)] public string DestinationNetwork { get; set; }
        [StringInputValidation] public string DestinationWallet { get; set; }

        [CollectionInput] public List<string> Paths { get; set; }
    }
}
