using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._BlockChain;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._Common.Services;
using CoinBank.Services._Price;
using CoinBank.Services._Swap.DTOs.Results;
using CoinBank.Services._Swap.DTOs.Updates;
using CoinBank.Services._Transaction._Hub;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Utilities.Exceptions.Common;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._Swap
{
    public class SwapService(ISwapRepository _swapRepository,
        IBlockChainService _blockChainService,
        IPriceService _priceService,
         ILogger<SwapService> _logger,
        IHubContext<WalletNotifyHub> _hubContext,
        AvailableTokensSettings _availableTokenDatas) : ISwapService, IScopedDependency
    {


        public async Task<SwapCreatedResult> CreateSwapAsync(CreateSwapUpdate update, string walletAddress, string publicKey)
        {

            ValidateDifferentTokens(update);
            var sourceTokenData = ValidateTokenInNetwork(update.SourceNetwork, update.SourceSymbol);
            var destinationTokenData = ValidateTokenInNetwork(update.DestinationNetwork, update.DestinationToken);


            #region Source
            var swapReference = IdGenerartor.GenerateBytes32HexId();
            var estEid = _blockChainService.MapNetworkToEid(update.SourceNetwork);
            var sourceNetwork = update.SourceNetwork;
            var sourceSymbol = update.SourceSymbol;
            var sourceTokenAddress = sourceTokenData.Address;
            var sourceTokenPrice = await _priceService.GetOneTokenPriceForInternalUsageAsync(sourceSymbol);
            var sourceAmount = update.SourceTokenAmount;
            var sourceAmountInWei = _blockChainService.ConvertToWei(sourceAmount, sourceTokenData.PriceDecimalPlaces);
            var sourceWallet = walletAddress;
            #endregion

            #region Destination
            var destinationNetwork = update.DestinationNetwork;
            var destinationSymbol = update.DestinationToken;
            var destinationTokenAddress = destinationTokenData.Address;
            var destinationWallet = update.DestinationWallet;
            var destinationTokenPrice = await _priceService.GetOneTokenPriceForInternalUsageAsync(destinationSymbol);
            #endregion

            var (fee, feeToken) = await _blockChainService.SwapGetEstimatedFeeAsync(new _BlockChain.DTOs.Updates.GetSwapEstimatedFeeUpdate
            {
                SwapReference = swapReference,
                DstEid = estEid,
                DestinationNetwork = update.DestinationNetwork,
                SourceTokenAddress = sourceTokenAddress,
                DestinationTokenAddress = destinationTokenAddress,
                SourceAmoutInWei = sourceAmountInWei,
                SourceNetwork = sourceNetwork,
                DestinationWallet = destinationWallet,
            });

            var destinationTokenOutAmountInWei = await _blockChainService.SwapGetOutputAmountAsync(new _BlockChain.DTOs.Updates.SwapGetOutputAmount
            {
                DestinationTokenAddress = destinationTokenAddress,
                SourceTokenAddress = sourceTokenAddress,
                SourceAmountInWei = sourceAmountInWei
            });
            var destinationAmount = _blockChainService.ConvertFromWei(destinationTokenOutAmountInWei, destinationTokenData.PriceDecimalPlaces);

            var swap = new Swap
            {
                SwapReference = swapReference,
                WalletAddress = walletAddress,
                UserPublicKey = publicKey,

                SourceNetwork = sourceNetwork,
                SourceSymbol = sourceSymbol,
                SourceTokenPrice = sourceTokenPrice,
                SourceAmount = sourceAmount,
                SourceAmountInWei = sourceAmountInWei.ToString(),
                SourceWallet = sourceWallet,

                DestinationNetwork = destinationNetwork,
                DestinationSymbol = destinationSymbol,
                DestinationAmount = destinationAmount,
                DestinationAmountInWei = destinationTokenOutAmountInWei.ToString(),
                DestinationTokenPrice = destinationTokenPrice,
                DestinationWallet = destinationWallet,

                Fee = fee,
                FeeToken = feeToken,
                State = SwapState.NotRegistered,

                RegisterHash = null,
                RegisterMoment = null,
                Transactions = [],
            };

            await _swapRepository.InsertOneAsync(swap);

            return new SwapCreatedResult
            {

                SwapReference = swap.SwapReference,
                WalletAddress = swap.WalletAddress,

                SourceNetwork = swap.SourceNetwork,
                SourceSymbol = swap.SourceSymbol,
                SourceTokenAddress = sourceTokenAddress,
                SourceTokenPrice = swap.SourceTokenPrice,
                SourceAmount = swap.SourceAmount,
                SourceAmountInWei = swap.SourceAmountInWei,
                SourceWallet = swap.SourceWallet,

                DstEid = estEid,

                DestinationNetwork = swap.DestinationNetwork,
                DestinationSymbol = swap.DestinationSymbol,
                DestinationTokenPrice = swap.DestinationTokenPrice,
                DestinationMinAmount = swap.DestinationAmount,
                DestinationMinAmountInWei = swap.DestinationAmountInWei,
                DestinationTokenAddress = destinationTokenAddress,
                DestinationWallet = swap.DestinationWallet,

                FeeToken = swap.FeeToken,
                Fee = swap.Fee,

                State = swap.State,
                Transactions = swap.Transactions ?? new List<SwapTransaction>(),
                CreatedMoment = swap.CreatedMoment,
                ModifiedMoment = swap.ModifiedMoment,
            };
        }

        public async Task<SwapListResult> GetSwapHistoryAsync(SwapHistoryUpdate update, string walletAddress, string publicKey)
        {
            var query = _swapRepository.AsQueryable();

            if (!string.IsNullOrWhiteSpace(update.Symbol))
            {
                var symbol = update.Symbol.ToUpper();

                query = query.Where(q =>
                    q.SourceSymbol == symbol ||
                    q.DestinationSymbol == symbol);
            }

            if (string.IsNullOrWhiteSpace(publicKey))
                throw new BadRequestException("Access denied!");

            if (publicKey == "guess")
            {
                query = query.Where(x =>
                    x.WalletAddress == walletAddress &&
                    x.State != SwapState.NotRegistered);
            }
            else
            {
                query = query.Where(x =>
                    x.UserPublicKey == publicKey &&
                    x.State != SwapState.NotRegistered);
            }

            var totalCount = await query.CountAsync();

            var page = update.Pagination?.Page ?? 1;
            var size = update.Pagination?.Size ?? 25;

            var data = await query
                .OrderByDescending(x => x.RegisterMoment)
                .Skip((page - 1) * size)
                .Take(size)
                .ToListAsync();

            var result = new SwapListResult
            {
                TotalCount = totalCount,
                PageCount = (int)Math.Ceiling((double)totalCount / size),
                Data = data.Select(ConvertToSwapResult).ToList()
            };

            return result;
        }

        public async Task<SwapResult> GetOneSwapByReferenceAsync(SwapReferenceUpdate update, string walletAddress, string publicKey)
        {

            if (string.IsNullOrWhiteSpace(publicKey))
                throw new BadRequestException("Access denied!");

            var query = _swapRepository.AsQueryable();

            query = query.Where(x =>
                x.SwapReference == update.SwapReference &&
                x.State != SwapState.NotRegistered);

            if (publicKey == "guess")
            {
                query = query.Where(x =>
                    x.WalletAddress == walletAddress);
            }
            else
            {
                query = query.Where(x =>
                    x.UserPublicKey == publicKey);
            }

            var swap = await query.FirstOrDefaultAsync();

            if (swap == null)
                throw new NotFoundException("Swap not found");

            return ConvertToSwapResult(swap);
        }

        public async Task RemoveNotRegisteredSwapsAsync()
        {
            var oneWeekAgo = DateTime.UtcNow.AddDays(-7);
            var query = _swapRepository.AsQueryable();
            var swapsToDelete = await query
                .Where(x =>
                    x.RegisterHash == null &&
                    x.State == SwapState.NotRegistered &&
                    x.CreatedMoment <= oneWeekAgo)
                .ToListAsync();

            if (swapsToDelete == null || swapsToDelete.Count == 0)
                return;

            var ids = swapsToDelete.Select(x => x.Id).ToList();

            await _swapRepository.DeleteManyAsync(x => ids.Contains(x.Id));
        }

        public async Task AddTransactionToSwapAsync(AddTransactionToSwapUpdate update)
        {

            if (update == null || string.IsNullOrWhiteSpace(update.SwapReference))
                return;

            var swap = await _swapRepository.AsQueryable().FirstOrDefaultAsync(q => q.SwapReference == update.SwapReference);
            if (swap == null) return;
            

            var tokenData = GetTokenWithAddressAndNetwork(update.TokenAddress, update.Network);
            var tokenAmount = _blockChainService.ConvertFromWei(update.Amount, tokenData.PriceDecimalPlaces);

            var transaction = new SwapTransaction
            {
                CreateMoment = DateTime.UtcNow,
                Hash = update.Hash,
                Network = update.Network,
                Symbol = tokenData.Name,
                Amount = tokenAmount,
                Type = update.Type
            };


            var filter = Builders<Swap>.Filter.Eq(x => x.SwapReference, update.SwapReference);

            var updateDefinition = Builders<Swap>.Update
                .Push(x => x.Transactions, transaction);

            await _swapRepository.FindOneAndUpdateAsync(filter, updateDefinition);

            string message = update.Type switch
            {
                SwapTransactionType.Init =>
                    $"Swap started: {swap.SourceAmount} {swap.SourceSymbol} → {swap.DestinationSymbol}.",

                SwapTransactionType.Execute =>
                    $"Swap completed: You received {swap.DestinationAmount} {swap.DestinationSymbol}.",

                SwapTransactionType.Failed =>
                    $"Swap failed: {swap.SourceSymbol} → {swap.DestinationSymbol}. Please try again.",

                _ => "Swap status updated."
            };

            try
            {
                await _hubContext.Clients.Group(swap.WalletAddress)
                    .SendAsync("SwapMessage", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send Swap notification for SwapReference {SwapReference}",
                    update.SwapReference);
            }


        }

        private AvailableTokenData GetTokenWithAddressAndNetwork(string tokenAddress, string network = null)
        {
            if (tokenAddress == null)
                throw new BadRequestException($"Unsupported token name! {tokenAddress}");

            if (network == null)
            {
                var tokenData = _availableTokenDatas.FirstOrDefault(q => q.Address.Equals(tokenAddress, StringComparison.OrdinalIgnoreCase))
              ?? throw new BadRequestException($"Unsupported token name! {tokenAddress}");
                return tokenData;
            }
            else
            {
                var tokenData = _availableTokenDatas.FirstOrDefault(q => q.Address.Equals(tokenAddress, StringComparison.OrdinalIgnoreCase)
                  && q.Network.Equals(network, StringComparison.OrdinalIgnoreCase))

              ?? throw new BadRequestException($"Unsupported token name and network! {tokenAddress} {network}");
                return tokenData;
            }
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

        private AvailableTokenData ValidateTokenInNetwork(string network, string token)
        {
            var tokenData = _availableTokenDatas.FirstOrDefault(q => q.Name == token.ToUpper() && q.Network == network.ToUpper());

            if (!tokenData.CanSwap) throw new BadRequestException($"Token  {token} In Network '{network}' is not supported for swap");

            if (tokenData == null)
                throw new BadRequestException($"Token  {token} In Network '{network}' is not supported.");
            return tokenData;
        }

        #endregion

        private SwapResult ConvertToSwapResult(Swap swap)
        {
            return new SwapResult
            {
                SwapReference = swap.SwapReference,
                WalletAddress = swap.WalletAddress,

                SourceNetwork = swap.SourceNetwork,
                SourceSymbol = swap.SourceSymbol,
                SourceTokenPrice = swap.SourceTokenPrice,
                SourceAmount = swap.SourceAmount,
                SourceAmountInWei = swap.SourceAmountInWei,
                SourceWallet = swap.SourceWallet,

                DestinationNetwork = swap.DestinationNetwork,
                DestinationSymbol = swap.DestinationSymbol,
                DestinationTokenPrice = swap.DestinationTokenPrice,
                DestinationAmount = swap.DestinationAmount,
                DestinationAmountInWei = swap.DestinationAmountInWei,
                DestinationWallet = swap.DestinationWallet,

                FeeToken = swap.FeeToken,
                Fee = swap.Fee,

                State = swap.State,
                CreatedMoment = swap.CreatedMoment,
                ModifiedMoment = swap.ModifiedMoment,
                Transactions = swap.Transactions ?? new List<SwapTransaction>()
            };
        }



    }
}
