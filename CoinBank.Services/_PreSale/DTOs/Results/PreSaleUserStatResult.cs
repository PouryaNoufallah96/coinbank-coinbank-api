namespace CoinBank.Services._PreSale.DTOs.Results
{
    public class PreSaleUserStatResult
    {
        public string PreSaleReference { get; set; }
        public int OrderCount { get; set; }
        public int TotalOrderCount { get; set; } = 5;
        public decimal TotalBought { get; set; }
        //public decimal RemainForUser { get; set; }
    }
}
