using CoinBank.Services._Swap.DTOs.Storages;
using Microsoft.AspNetCore.SignalR;

namespace CoinBank.Services._Swap._Hub
{
    public class SwapHub : Hub
    {
        private readonly SwapStorage _swapStorage;

        public SwapHub(SwapStorage swapStorage)
        {
            _swapStorage = swapStorage;
        }

        public override async Task OnConnectedAsync()
        {

            await Clients.Caller.SendAsync("NotifySwap", _swapStorage);
            await base.OnConnectedAsync();
        }

        public async Task InventoryNotify()
        {
            await Clients.All.SendAsync("NotifySwap", _swapStorage);
        }

    }
}
