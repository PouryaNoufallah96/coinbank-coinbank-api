//using CoinBank.Domain.Repositories.Contracts;
//using CoinBank.Services._Swap.DTOs.Results;
//using CoinBank.Services._Swap.DTOs.Settings;
//using CoinBank.Services._Swap.DTOs.Updates;
//using Utilities.Exceptions.Common;
//using static Utilities.Constants.RegisterMode;

//namespace CoinBank.Services._Swap
//{
//    public class SwapService(ISwapRepository _swapRepository, SwapSetting _swapSetting) : ISwapService, IScopedDependency
//    {
//        public async Task<SwapResult> CreateSwapAsync(CreateSwapUpdate update, string walletAddress, string publicKey)
//        {

//            ValidateDifferentTokens(update);
//            ValidateTokenInNetwork(update.SourceNetwork, update.SourceSymbol);
//            ValidateTokenInNetwork(update.DestinationNetwork, update.DestinationToken);

//            // TODO: pricing, fee, signature, db save ...

//            //return new SwapResult
//            //{
//            //    Success = true,
//            //    WalletAddress = walletAddress,
//            //    SourceSymbol = update.SourceSymbol,
//            //    SourceNetwork = update.SourceNetwork,
//            //    DestinationSymbol = update.DestinationToken,
//            //    DestinationNetwork = update.DestinationNetwork
//            //};
//        }

//        #region Validation Methods

      

//        private void ValidateDifferentTokens(CreateSwapUpdate update)
//        {
//            if (update.SourceSymbol.Equals(update.DestinationToken, StringComparison.OrdinalIgnoreCase)
//                && update.SourceNetwork.Equals(update.DestinationNetwork, StringComparison.OrdinalIgnoreCase))
//            {
//                throw new BadRequestException("Source and destination tokens cannot be the same.");
//            }
//        }

//        private void ValidateTokenInNetwork(string network, string token)
//        {
//            if (!_swapSetting.ContainsKey(network))
//                throw new BadRequestException($"Network '{network}' is not supported.");

//            var networkSetting = _swapSetting[network];

//            if (networkSetting.AvailableTokens == null ||
//                !networkSetting.AvailableTokens.Any(t => t.Equals(token, StringComparison.OrdinalIgnoreCase)))
//            {
//                throw new BadRequestException($"Token '{token}' is not available on network '{network}'.");
//            }
//        }

//        #endregion




//    }
//}
