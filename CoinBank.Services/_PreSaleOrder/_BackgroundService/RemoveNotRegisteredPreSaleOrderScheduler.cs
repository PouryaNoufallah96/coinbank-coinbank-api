using Microsoft.Extensions.DependencyInjection;
using Utilities.Services;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._PreSaleOrder._BackgroundService
{
    public class RemoveNotRegisteredPreSaleOrderScheduler(IServiceProvider serviceProvider) : SchedulerBase(serviceProvider, TimeSpan.FromDays(1)), IHostedDependency
    {
        protected override async Task HandleAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken)
        {
            var preSaleOrderService = scopedProvider.GetRequiredService<IPreSaleOrderService>();
            await preSaleOrderService.RemoveNotRegisteredPreSaleOrderAsync();
        }
    }
}
