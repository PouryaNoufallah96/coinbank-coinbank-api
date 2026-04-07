using Microsoft.Extensions.DependencyInjection;
using Utilities.Services;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._PreSale._BackgroundServices
{
    public class SyncExpirePreSaleTokenScheduler(IServiceProvider serviceProvider) : SchedulerBase(serviceProvider, TimeSpan.FromHours(3)), IHostedDependency
    {
        protected override async Task HandleAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken)
        {
            var preSaleService = scopedProvider.GetRequiredService<IPreSaleService>();
            await preSaleService.SyncExpirePreSaleTokenAsync();
        }
    }
}
