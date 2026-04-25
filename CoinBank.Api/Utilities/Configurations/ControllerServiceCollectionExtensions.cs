using CoinBank.Services._BlockChain.DTOs.Settings;
using CoinBank.Services._BlockChainWebSocket.DTOs;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._File.DTOs.Settings;
using CoinBank.Services._Price.DTOs.Settings;
using CoinBank.Services._Stake.DTOs.Settings;
using CoinBank.Services._Swap.DTOs.Settings;
using CoinBank.Services._User.DTOs.Settings;
using Microsoft.Extensions.Options;


namespace CoinBank.Api.Utilities.Configurations
{
    public static class ControllerServiceCollectionExtensions
    {
        public static void AddSettings(this IServiceCollection services, IConfiguration configuration)
        {

            services.RegisterSetting<AvailableTokensSettings>(configuration.GetSection(nameof(AvailableTokensSettings)));
            services.RegisterSetting<BlockchainWebSocketSetting>(configuration.GetSection(nameof(BlockchainWebSocketSetting)));
            services.RegisterSetting<BlockChainSettings>(configuration.GetSection(nameof(BlockChainSettings)));
            services.RegisterSetting<StakeSetting>(configuration.GetSection(nameof(StakeSetting)));
            services.RegisterSetting<FileSettings>(configuration.GetSection(nameof(FileSettings)));
            services.RegisterSetting<PriceSetting>(configuration.GetSection(nameof(PriceSetting)));
            services.RegisterSetting<VerifyTronServiceSettings>(configuration.GetSection(nameof(VerifyTronServiceSettings)));
            services.RegisterSetting<SwapSetting>(configuration.GetSection(nameof(SwapSetting)));
        }

        private static void RegisterSetting<TSettings>(this IServiceCollection services, IConfigurationSection configuration)
           where TSettings : class, new()
        {
            services.Configure<TSettings>(configuration);
            services.AddSingleton(sp => sp.GetRequiredService<IOptions<TSettings>>().Value);
        }

        private static void RegisterSetting<TSettings, TISettings>(this IServiceCollection services, IConfigurationSection configuration)
            where TISettings : class
            where TSettings : class, TISettings, new()
        {
            services.Configure<TSettings>(configuration);
            services.AddSingleton<TISettings>(sp => sp.GetRequiredService<IOptions<TSettings>>().Value);
        }
    }
}
