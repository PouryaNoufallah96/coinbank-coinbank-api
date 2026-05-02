using Microsoft.Extensions.Hosting;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Swap._BackgroundServices
{
    public class SwapStorageInitializerBackgoundService(ISwapService swapService) : BackgroundService, IHostedDependency
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await swapService.InitializeSwapStorageAsync();
        }
    }
}
