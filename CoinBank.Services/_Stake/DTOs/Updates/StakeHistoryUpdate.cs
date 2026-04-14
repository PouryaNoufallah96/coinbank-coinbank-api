using Utilities.DTOs;

namespace CoinBank.Services._Stake.DTOs.Updates
{
    public class StakeHistoryUpdate
    {
        public Pagination Pagination { get; set; } = new Pagination();
        public string Symbol { get; set; } = null;
    }
}
