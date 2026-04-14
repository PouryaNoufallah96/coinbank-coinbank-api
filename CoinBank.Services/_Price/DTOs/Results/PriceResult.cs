namespace CoinBank.Services._Price.DTOs.Results
{
    public class PriceResult
    {
        public string TokenName { get; set; }
        public string TokenNetwork { get; set; }
        public decimal Price { get; set; }
        public decimal ChangePrice24hPercentage { get; set; }
    }
}
