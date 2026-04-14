using CoinBank.Domain.Collections;
using CoinBank.Services._PreSale._Hub;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._PreSale.DTOs.Storages
{
    public class PreSaleStorage : ConcurrentDictionary<string, PreSaleData>, ISelfSingletonDependency
    {
        private readonly IHubContext<PreSaleHub> _hubContext;

        public PreSaleStorage(IHubContext<PreSaleHub> hubContext)
        {
            _hubContext = hubContext;
        }

        /// <summary>
        /// Add or Update (replace whole object)
        /// </summary>
        public void Upsert(string preSaleReference, PreSaleData data)
        {
            data.LastUpdated = DateTime.UtcNow;

            AddOrUpdate(
                preSaleReference,
                data,
                (key, existing) => data 
            );

            _hubContext.Clients.All.SendAsync("NotifyPreSale", this);
        }

        /// <summary>
        /// Initialize storage with list (bulk load)
        /// </summary>
        public void Init(List<PreSaleData> items)
        {
            this.Clear();

            foreach (var item in items)
            {
                item.LastUpdated = DateTime.UtcNow;

                this.TryAdd(item.PreSaleReference, item);
            }

            _hubContext.Clients.All.SendAsync("NotifyPreSale", this);
        }


    }


    public class PreSaleData 
    {
        public DateTime CreatedMoment { get; set; }
        public DateTime? ModifiedMoment { get; set; }
        public string PreSaleReference { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public string LogoUrl { get; set; }
        public string Description { get; set; }

        public decimal TotalSupply { get; set; } 
        public decimal MaxPerOrder { get; set; } 
        public decimal MinPerOrder { get; set; }
        public decimal TotalSupplied { get; set; }
        public decimal AvailableForEachOrder { get; set; }
        public decimal Price { get; set; } 
        public DateTime StartSellingAt { get; set; }
        public DateTime EndSellingAt { get; set; }
        public List<PreSaleReleaseStep> ReleaseSchedule { get; set; }
        public PreSaleState State { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
