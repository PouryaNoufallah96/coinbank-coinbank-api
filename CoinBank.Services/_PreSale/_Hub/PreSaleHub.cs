using CoinBank.Services._PreSale.DTOs.Storages;
using Microsoft.AspNetCore.SignalR;


namespace CoinBank.Services._PreSale._Hub
{
    public class PreSaleHub : Hub
    {
        private readonly PreSaleStorage _preSaleStorage;

        public PreSaleHub(PreSaleStorage preSaleStorage)
        {
            _preSaleStorage = preSaleStorage;
        }

        public override async Task OnConnectedAsync()
        {

            await Clients.Caller.SendAsync("NotifyPreSale", _preSaleStorage);
            await base.OnConnectedAsync();
        }

        public async Task InventoryNotify()
        {
            await Clients.All.SendAsync("NotifyPreSale", _preSaleStorage);
        }

    }
}
