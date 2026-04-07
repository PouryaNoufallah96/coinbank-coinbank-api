namespace CoinBank.Services._PreSaleOrder.DTOs.Results
{
    public class PreSaleOrderListResult
    {
        public List<PreSaleOrderResult> Data { get; set; } = [];
        public int PageCount { get; set; } = 0;
        public int TotalCount { get; set; } = 0;
    }
}
