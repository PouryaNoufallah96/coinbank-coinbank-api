using Utilities.Attributes;

namespace CoinBank.Services._Withdrawal.DTOs.Updates
{
    public class WithdrawStakeAmountUpdate
    {
        [StringInputValidation] public string StakeReference { get; set; }
        [NumericInputValidation(isRequired: true, mustBeNonZero: true)] public decimal TokenAmount { get; set; }

    }
}
