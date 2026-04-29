using Utilities.Attributes;
using Utilities.DTOs;

namespace CoinBank.Services._Swap.DTOs.Updates
{
    public class SwapHistoryUpdate
    {
        public Pagination Pagination { get; set; }
        public string Symbol { get; set; } = null;

    }


    public class SwapReferenceUpdate
    {
        [StringInputValidation] public string SwapReference { get; set; }
    }
}
