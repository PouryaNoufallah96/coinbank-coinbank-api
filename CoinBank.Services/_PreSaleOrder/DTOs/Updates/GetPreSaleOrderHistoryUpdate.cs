using Utilities.DTOs;

namespace CoinBank.Services._PreSaleOrder.DTOs.Updates
{
    public class GetPreSaleOrderHistoryUpdate 
    {
        public Pagination Pagination { get; set; }
        public string Symbol { get; set; } = null;
    }
}
 