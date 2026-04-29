using CoinBank.Services._Swap;
using Microsoft.Extensions.DependencyInjection;
using Utilities.Services;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Stake._BackgroundSerives
{
    public class RemoveNotRegisteredSwapsScheduler(IServiceProvider serviceProvider) : SchedulerBase(serviceProvider, TimeSpan.FromDays(1)), IHostedDependency
    {
        protected override async Task HandleAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken)
        {
            var swapService = scopedProvider.GetRequiredService<ISwapService>();
            await swapService.RemoveNotRegisteredSwapsAsync();
        }
    }
}
