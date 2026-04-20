using Microsoft.AspNetCore.SignalR;

namespace CoinBank.Services._Transaction._Hub
{
    public class WalletNotifyHub : Hub
    {
        public override async Task OnConnectedAsync()
        {

            await base.OnConnectedAsync();
        }

        public async Task RegisterWallet(string walletAddress)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, walletAddress);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }

    }
}
