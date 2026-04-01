using CoinBank.Services._User.DTOs.Settings;
using Utilities.Attributes;

namespace CoinBank.Services._User.DTOs.Updates
{
    public class GetTokenWithPureWalletAddress
    {
        [StringInputValidation] public string WalletAddress { get; set; }
        [EnumInputValidation] public WalletType WalletType { get; set; }
        [StringInputValidation] public string ClientId { get; set; }
        [StringInputValidation] public string ClientSecret { get; set; }
    }
}
