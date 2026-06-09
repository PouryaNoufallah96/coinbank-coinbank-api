using Microsoft.Extensions.DependencyInjection;
using Utilities.Services;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Price._BackgroundServices
{
    public class TokenHistoryScheduler(IServiceProvider serviceProvider) : SchedulerBase(serviceProvider, TimeSpan.FromHours(3)), IHostedDependency
    {
        protected override async Task HandleAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken)
        {
            var priceService = scopedProvider.GetRequiredService<IPriceService>();
            await priceService.SyncCoinHistoryTokenFromCoinMarketCapAsync();
        }
    }
}
