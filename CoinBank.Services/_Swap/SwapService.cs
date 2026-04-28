using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._Swap.DTOs.Results;
using CoinBank.Services._Swap.DTOs.Updates;
using Utilities.Exceptions.Common;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Swap
{
    public class SwapService(ISwapRepository _swapRepository, AvailableTokensSettings _availableTokenDatas) : ISwapService, IScopedDependency
    {
        public async Task<SwapResult> CreateSwapAsync(CreateSwapUpdate update, string walletAddress, string publicKey)
        {

            ValidateDifferentTokens(update);
            ValidateTokenInNetwork(update.SourceNetwork, update.SourceSymbol);
            ValidateTokenInNetwork(update.DestinationNetwork, update.DestinationToken);
             
            // TODO: pricing, fee, signature, db save ...

            //return new SwapResult
            //{
            //    Success = true,
            //    WalletAddress = walletAddress,
            //    SourceSymbol = update.SourceSymbol,
            //    SourceNetwork = update.SourceNetwork,
            //    DestinationSymbol = update.DestinationToken,
            //    DestinationNetwork = update.DestinationNetwork
            //};
        }

        #region Validation Methods



        private void ValidateDifferentTokens(CreateSwapUpdate update)
        {
            if (update.SourceSymbol.Equals(update.DestinationToken, StringComparison.OrdinalIgnoreCase)
                && update.SourceNetwork.Equals(update.DestinationNetwork, StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("Source and destination tokens with networks cannot be the same.");
            }
        }

        private void ValidateTokenInNetwork(string network, string token)
        {
            var tokenData = _availableTokenDatas.FirstOrDefault(q => q.Name == token.ToUpper() && q.Network == network.ToUpper());

            if (tokenData == null)
                throw new BadRequestException($"Token  {token} In Network '{network}' is not supported.");
          
        }

        #endregion




    }
}
