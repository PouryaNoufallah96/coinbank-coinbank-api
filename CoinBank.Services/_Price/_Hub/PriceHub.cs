using CoinBank.Services._Price.DTOs.Storages;
using Microsoft.AspNetCore.SignalR;

namespace CoinBank.Services._Price._Hub
{
    public class PriceHub : Hub
    {
        private readonly PriceStorage _priceStorage;

        public PriceHub(PriceStorage priceStorage)
        {
            _priceStorage = priceStorage;
        }

        public override async Task OnConnectedAsync()
        {

            await Clients.Caller.SendAsync("NotifyPrice", _priceStorage);
            await base.OnConnectedAsync();
        }       

    }
}
