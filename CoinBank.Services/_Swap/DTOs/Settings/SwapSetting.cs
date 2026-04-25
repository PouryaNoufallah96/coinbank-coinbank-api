namespace CoinBank.Services._Swap.DTOs.Settings
{
    public class SwapSetting : Dictionary<string, SwapSettinItem> //key is network
    {
    }

    public class SwapSettinItem
    {
        public string ContractAddress { get; set; }
        public List<string> AvailableTokens { get; set; }

    }


}
