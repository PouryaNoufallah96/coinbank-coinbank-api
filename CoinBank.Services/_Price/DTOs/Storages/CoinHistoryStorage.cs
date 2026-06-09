using System.Collections.Concurrent;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Price.DTOs.Storages
{


    // key is token name
    public class CoinHistoryStorage : ConcurrentDictionary<string, CoinHistoryData>, ISelfSingletonDependency
    {

    }

    public class CoinHistoryData
    {
        public List<CoinCandle> Daily { get; set; } = [];
        public List<CoinCandle> Weekly { get; set; } = [];
        public List<CoinCandle> Annually { get; set; } = [];
    }

    public class CoinCandle
    {
        public DateTime Time { get; set; }
        public decimal Open { get; set; }
        public decimal Close { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Volume { get; set; }
    }
}
