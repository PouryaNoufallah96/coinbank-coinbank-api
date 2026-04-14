//using Microsoft.Extensions.DependencyInjection;
//using Utilities.Services;
//using static Utilities.Constants.RegisterMode;

//namespace CoinBank.Services._PreSale._BackgroundServices
//{
//    public class SyncCompletedPreSalesScheduler(IServiceProvider serviceProvider) : SchedulerBase(serviceProvider, TimeSpan.FromHours(2)), IHostedDependency
//    {
//        protected override async Task HandleAsync(IServiceProvider scopedProvider, CancellationToken cancellationToken)
//        {
//            var preSaleService = scopedProvider.GetRequiredService<IPreSaleService>();
//            await preSaleService.SyncCompletedPreSalesAsync();
//        }
//    }
//}
