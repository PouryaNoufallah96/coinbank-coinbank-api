using Utilities.Attributes;

namespace CoinBank.Services._Stake.DTOs.Updates
{
    public class StakeDetailUpdate
    {
        [StringInputValidation] public string StakeReference { get; set; }
    }
}
