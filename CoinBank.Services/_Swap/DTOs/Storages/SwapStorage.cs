using CoinBank.Services._Swap._Hub;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Swap.DTOs.Storages
{
    public class SwapStorage : ConcurrentDictionary<string, SwapData> , ISelfSingletonDependency
    {
        private readonly IHubContext<SwapHub> _hubContext;

        public SwapStorage(IHubContext<SwapHub> hubContext)
        {
            _hubContext = hubContext;
        }


        public void Upsert(string tokenAddress, SwapData data)
        {
            data.LastUpdated = DateTime.UtcNow;

            if (data.ContractBalance < data.MaxSwapAmount)
                data.MaxSwapAmount = data.ContractBalance;

            AddOrUpdate(tokenAddress,
                data,
                (key, existing) =>
                {
                    lock (existing)
                    {
                        existing.Name = data.Name;
                        existing.Network = data.Network;
                        existing.Symbol = data.Symbol;
                        existing.ContractBalance = data.ContractBalance;

                        existing.MaxSwapAmount = data.ContractBalance < data.MaxSwapAmount
                            ? data.ContractBalance
                            : data.MaxSwapAmount;

                        existing.MinSwapAmount = data.MinSwapAmount;
                        existing.LastUpdated = DateTime.UtcNow;
                    }

                    return existing;
                });
            Broadcast();
        }

        public void Sync(Dictionary<string, SwapData> items)
        {
            foreach (var kv in items)
            {
                kv.Value.LastUpdated = DateTime.UtcNow;

                if (kv.Value.ContractBalance < kv.Value.MaxSwapAmount)
                    kv.Value.MaxSwapAmount = kv.Value.ContractBalance;

                this.AddOrUpdate(
                    kv.Key,
                    kv.Value,
                    (key, existing) =>
                    {
                        lock (existing)
                        {
                            existing.Name = kv.Value.Name;
                            existing.Network = kv.Value.Network;
                            existing.Symbol = kv.Value.Symbol;
                            existing.ContractBalance = kv.Value.ContractBalance;

                            existing.MaxSwapAmount = kv.Value.ContractBalance < kv.Value.MaxSwapAmount
                                ? kv.Value.ContractBalance
                                : kv.Value.MaxSwapAmount;

                            existing.MinSwapAmount = kv.Value.MinSwapAmount;
                            existing.LastUpdated = DateTime.UtcNow;
                        }

                        return existing;
                    });
            }

            Broadcast();
        }

        public bool TryGetSwap(string tokenAddress, out SwapData data)
        {
            data = null;

            if (string.IsNullOrWhiteSpace(tokenAddress))
                return false;

            return this.TryGetValue(tokenAddress, out data);
        }

        public bool UpdateContractBalance(string tokenAddress, decimal newBalance)
        {
            if (string.IsNullOrWhiteSpace(tokenAddress))
                return false;

            if (!this.TryGetValue(tokenAddress, out var existing))
                return false;

            lock (existing)
            {
                existing.ContractBalance = newBalance;
                existing.LastUpdated = DateTime.UtcNow;
            }

            Broadcast();
            return true;
        }
       
        public List<SwapData> GetAll()
        {
            return this.Values.ToList();
        }

        private void Broadcast()
        {
            _ = _hubContext.Clients.All.SendAsync("NotifySwap", this);
        }

    }

    public class SwapData
    {
        public string Name { get; set; }
        public string Network { get; set; }
        public string Symbol { get; set; }
        public decimal ContractBalance { get; set; }
        public decimal MaxSwapAmount { get; set; }
        public decimal MinSwapAmount { get; set; }
        public DateTime LastUpdated { get; set; }
    }

}
