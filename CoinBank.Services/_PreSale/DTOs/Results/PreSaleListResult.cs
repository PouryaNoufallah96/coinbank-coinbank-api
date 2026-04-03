namespace CoinBank.Services._PreSale.DTOs.Results
{
    public class PreSaleListResult
    {
        public List<PreSaleResult> Data { get; set; } = [];
        public int PageCount { get; set; } = 0;
        public int TotalCount { get; set; } = 0;
    }
}
