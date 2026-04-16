using Microsoft.Extensions.Hosting;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._PreSale._BackgroundServices
{
    public class InitPreSaleHubBackgroundService
    
        (IPreSaleService presaleService) : BackgroundService, IHostedDependency
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await presaleService.InitializePreSaleStorageAsync();
        }
    }
}
