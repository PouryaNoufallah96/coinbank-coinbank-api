using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._BlockChain;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._Common.Services;
using CoinBank.Services._Price;
using CoinBank.Services._Swap.DTOs.Results;
using CoinBank.Services._Swap.DTOs.Storages;
using CoinBank.Services._Swap.DTOs.Updates;
using CoinBank.Services._Transaction._Hub;
using Microsoft.AspNetCore.SignalR;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
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
        SwapStorage _swapStorage,
        IHubContext<WalletNotifyHub> _hubContext,
        AvailableTokensSettings _availableTokenDatas) : ISwapService, IScopedDependency
    {

        public async Task<SwapCreatedResult> CreateSwapAsync(CreateSwapUpdate update, string EVMwalletAddress, string TronWalletAddress, string publicKey)
        {

            var walletAddress = SpesifyWalletAddress(EVMwalletAddress, TronWalletAddress, update.SourceNetwork.ToUpper());

            ValidateDifferentTokens(update);
            var sourceTokenData = GetAndValidateSwappableToken(update.SourceNetwork, update.SourceSymbol);
            var destinationTokenData = GetAndValidateSwappableToken(update.DestinationNetwork, update.DestinationToken);
            await ValidateBalancesForSwapAsync(update, walletAddress);

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

        private string SpesifyWalletAddress(string EVMwalletAddress, string TromWalletAddress, string network)
        {
            if (network == "BEP20")
            {
                if (EVMwalletAddress.IsNullOrEmpty()) throw new BadRequestException("Please sign with your BSC wallet");
                return EVMwalletAddress;
            }
            else if (network == "ERC20")
            {
                if (EVMwalletAddress.IsNullOrEmpty()) throw new BadRequestException("Please sign with your ETH wallet");
                return EVMwalletAddress;

            }
            else if (network == "TRC20")
            {
                if (TromWalletAddress.IsNullOrEmpty()) throw new BadRequestException("Please sign with your TRON wallet");
                return TromWalletAddress;

            }
            else throw new BadRequestException("Wrong network!");


        }

        public async Task<SwapListResult> GetSwapHistoryAsync(SwapHistoryUpdate update, string EVMwalletAddress, string TronWalletAddress, string publicKey)
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
                    (x.WalletAddress == EVMwalletAddress || x.WalletAddress == TronWalletAddress) 
                    && x.State != SwapState.NotRegistered);
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

        public async Task<SwapResult> GetOneSwapByReferenceAsync(SwapReferenceUpdate update, string EVMwalletAddress, string TronWalletAddress, string publicKey)
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
                    x.WalletAddress == EVMwalletAddress || x.WalletAddress == TronWalletAddress);
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

            var baseFilter = Builders<Swap>.Filter.Eq(x => x.SwapReference, update.SwapReference);

            var newState = update.Type switch
            {
                SwapTransactionType.Init => SwapState.Pending,
                SwapTransactionType.Execute => SwapState.Completed,
                SwapTransactionType.Failed => SwapState.Failed,
                _ => (SwapState?)null
            };

            var updateBuilder = Builders<Swap>.Update
                .Push(x => x.Transactions, transaction);

            if (newState.HasValue)
                updateBuilder = updateBuilder.Set(x => x.State, newState.Value);

            FilterDefinition<Swap> finalFilter = baseFilter;

            if (update.Type == SwapTransactionType.Execute)
            {
                var amountFilter = Builders<Swap>.Filter.Lt(x => x.DestinationAmount, tokenAmount);
                finalFilter = Builders<Swap>.Filter.And(baseFilter, amountFilter);

                updateBuilder = updateBuilder.Set(x => x.DestinationAmount, tokenAmount);
            }

            var options = new FindOneAndUpdateOptions<Swap>
            {
                ReturnDocument = ReturnDocument.After
            };

            var updatedSwap = await _swapRepository.FindOneAndUpdateWithOptionAsync(finalFilter, updateBuilder, options);

            if (updatedSwap == null && update.Type == SwapTransactionType.Execute)
            {
                updatedSwap = await _swapRepository.FindOneAndUpdateWithOptionAsync(
                    baseFilter,
                    Builders<Swap>.Update
                        .Push(x => x.Transactions, transaction)
                        .Set(x => x.State, SwapState.Completed),
                    options
                );
            }

            if (updatedSwap == null)
                return;

            string message = update.Type switch
            {
                SwapTransactionType.Init =>
                    $"Swap started: {updatedSwap.SourceAmount} {updatedSwap.SourceSymbol} → {updatedSwap.DestinationSymbol}.",

                SwapTransactionType.Execute =>
                    $"Swap completed: You received {updatedSwap.DestinationAmount} {updatedSwap.DestinationSymbol}.",

                SwapTransactionType.Failed =>
                    $"Swap failed: {updatedSwap.SourceSymbol} → {updatedSwap.DestinationSymbol}. Please try again.",

                _ => "Swap status updated."
            };

            if (update.Type == SwapTransactionType.Execute)
            {
                await UpdateSingleTokenInStorageAsync(update.TokenAddress, update.Network);
            }

            try
            {
                await _hubContext.Clients.Group(updatedSwap.WalletAddress)
                    .SendAsync("SwapMessage", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send Swap notification for SwapReference {SwapReference}",
                    update.SwapReference);
            }
        }

        public async Task InitializeSwapStorageAsync()
        {
            try
            {

                var swappableTokens = _availableTokenDatas
                    .Where(t => t.CanSwap)
                    .ToList();

                var result = new Dictionary<string, SwapData>();


                var trc20Tokens = swappableTokens
                    .Where(t => t.Network == "TRC20")
                    .ToList();

                var bep20Tokens = swappableTokens
                    .Where(t => t.Network == "BEP20")
                    .ToList();

                var erc20Tokens = swappableTokens
                    .Where(t => t.Network == "ERC20")
                    .ToList();



                // TRC20
                Dictionary<string, decimal> trc20Balances = new();
                if (trc20Tokens.Any())
                {
                    trc20Balances = await _blockChainService
                        .GetTRC20ContractBalancesTronScanAsync(trc20Tokens.Select(t => t.Name).ToList());
                }

                // BEP20
                Dictionary<string, decimal> bep20Balances = new();
                if (bep20Tokens.Any())
                {
                    bep20Balances = await _blockChainService
                        .GetBep20SwapContractBalancesAsync(bep20Tokens.Select(t => t.Name).ToList());
                }

                // ERC20
                Dictionary<string, decimal> erc20Balances = new();
                if (erc20Tokens.Any())
                {
                    erc20Balances = await _blockChainService
                        .GetERC20SwapContractBalancesAsync(erc20Tokens.Select(t => t.Name).ToList());
                }


                foreach (var token in swappableTokens)
                {
                    decimal balance = 0;

                    switch (token.Network)
                    {
                        case "TRC20":
                            trc20Balances.TryGetValue(token.Name, out balance);
                            break;

                        case "BEP20":
                            bep20Balances.TryGetValue(token.Name, out balance);
                            break;

                        case "ERC20":
                            erc20Balances.TryGetValue(token.Name, out balance);
                            break;
                    }

                    var swapData = new SwapData
                    {
                        Name = token.Name,
                        Symbol = token.Name,
                        Network = token.Network,
                        ContractBalance = balance,
                        MinSwapAmount = token.MinSwapAmount,
                        MaxSwapAmount = token.MaxSwapAmount,
                        LastUpdated = DateTime.UtcNow
                    };

                    result[token.Address] = swapData;
                }


                _swapStorage.Sync(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SwapStorage initialization failed");
            }
        }

        public async Task UpdateSingleTokenInStorageAsync(string tokenAddress, string network)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tokenAddress) || string.IsNullOrWhiteSpace(network))
                    return;

                var token = _availableTokenDatas.FirstOrDefault(t =>
                    t.CanSwap &&
                    t.Address == tokenAddress &&
                    t.Network == network);

                if (token == null)
                {
                    _logger.LogWarning("Token not found for update: {Symbol} - {Network}", tokenAddress, network);
                    return;
                }

                decimal balance = 0;


                switch (token.Network)
                {
                    case "TRC20":
                        var trc20 = await _blockChainService
                            .GetTRC20ContractBalancesTronScanAsync(new List<string> { token.Name });

                        trc20.TryGetValue(token.Name, out balance);
                        break;

                    case "BEP20":
                        var bep20 = await _blockChainService
                            .GetBep20SwapContractBalancesAsync(new List<string> { token.Name });

                        bep20.TryGetValue(token.Name, out balance);
                        break;

                    case "ERC20":
                        var erc20 = await _blockChainService
                            .GetERC20SwapContractBalancesAsync(new List<string> { token.Name });

                        erc20.TryGetValue(token.Name, out balance);
                        break;

                    default:
                        _logger.LogWarning("Unsupported network: {Network}", token.Network);
                        return;
                }

                var key = token.Address;

                var data = new SwapData
                {
                    Name = token.Name,
                    Symbol = token.Name,
                    Network = token.Network,
                    ContractBalance = balance,
                    MinSwapAmount = token.MinSwapAmount,
                    MaxSwapAmount = token.MaxSwapAmount,
                    LastUpdated = DateTime.UtcNow
                };

                _swapStorage.Upsert(key, data);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateSingleAsync failed for {Symbol} - {Network}", tokenAddress, network);
            }
        }



        #region Private Methods
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

        private void ValidateDifferentTokens(CreateSwapUpdate update)
        {
            if (update.SourceSymbol.Equals(update.DestinationToken, StringComparison.OrdinalIgnoreCase)
                && update.SourceNetwork.Equals(update.DestinationNetwork, StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("Source and destination tokens with networks cannot be the same.");
            }
        }

        private async Task ValidateBalancesForSwapAsync(CreateSwapUpdate update, string userWallet)
        {
            if (string.IsNullOrWhiteSpace(userWallet))
                throw new Exception("User wallet is required");

            var sourceToken = _availableTokenDatas.FirstOrDefault(t =>
                t.Name == update.SourceSymbol && t.Network == update.SourceNetwork);

            var destinationToken = _availableTokenDatas.FirstOrDefault(t =>
                t.Name == update.DestinationToken && t.Network == update.DestinationNetwork);

            if (sourceToken == null)
                throw new Exception("Source token not supported");

            if (destinationToken == null)
                throw new Exception("Destination token not supported");

            decimal userBalance = 0;

            if (sourceToken.Network == "TRC20" && sourceToken.Name == "USDT")
            {
                userBalance = await _blockChainService.GetTRC20UsdtBalanceAsync(userWallet);
            }
            else if (sourceToken.Network == "BEP20")
            {
                userBalance = await _blockChainService
                    .GetBEP20WalletAddressSingleTokenBalanceAsync(userWallet, sourceToken.Name);
            }
            else if (sourceToken.Network == "ERC20" && sourceToken.Name == "USDT")
            {
                userBalance = await _blockChainService
                    .GetERC20WalletAddressSingleTokenBalanceAsync(userWallet, sourceToken.Name);
            }
            else
            {
                throw new Exception("Unsupported network for source token");
            }

            if (userBalance < update.SourceTokenAmount)
                throw new Exception("Insufficient balance");


            var sourcePrice = await _priceService.GetOneTokenPriceForInternalUsageAsync(sourceToken.Name);
            var destinationPrice = await _priceService.GetOneTokenPriceForInternalUsageAsync(destinationToken.Name);

            if (sourcePrice <= 0 || destinationPrice <= 0)
                throw new Exception("Price error");

            var destinationAmount = (update.SourceTokenAmount * sourcePrice) / destinationPrice;


            decimal contractBalance = 0;
            bool hasCached = false;



            if (_swapStorage.TryGetSwap(destinationToken.Name, out var cached))
            {
                contractBalance = cached.ContractBalance;
                hasCached = true;
            }



            if (!hasCached || contractBalance < destinationAmount)
            {
                Dictionary<string, decimal> contractBalances;

                if (destinationToken.Network == "TRC20")
                {
                    contractBalances = await _blockChainService.GetTRC20ContractBalancesTronScanAsync(
                        new List<string> { destinationToken.Name });
                }
                else if (destinationToken.Network == "BEP20")
                {
                    contractBalances = await _blockChainService.GetBep20SwapContractBalancesAsync(
                        new List<string> { destinationToken.Name });
                }
                else if (destinationToken.Network == "ERC20")
                {
                    contractBalances = await _blockChainService.GetERC20SwapContractBalancesAsync(
                        new List<string> { destinationToken.Name });
                }
                else
                {
                    throw new Exception("Unsupported destination network");
                }

                if (!contractBalances.TryGetValue(destinationToken.Name, out contractBalance))
                    throw new Exception("Destination token liquidity not found");


                _swapStorage.UpdateContractBalance(destinationToken.Address, contractBalance);

            }

            if (contractBalance < destinationAmount)
                throw new Exception("Insufficient liquidity");
        }


        private AvailableTokenData GetAndValidateSwappableToken(string network, string token, decimal? amount = null)
        {
            var tokenData = _availableTokenDatas.FirstOrDefault(q => q.Name == token.ToUpper() && q.Network == network.ToUpper());

            if (!tokenData.CanSwap) throw new BadRequestException($"Token  {token} In Network '{network}' is not supported for swap");

            if (tokenData == null)
                throw new BadRequestException($"Token  {token} In Network '{network}' is not supported.");

            if (amount.HasValue)
            {
                if (amount.Value < tokenData.MinSwapAmount)
                    throw new BadRequestException(
                        $"Minimum swap amount for {token} on {network} is {tokenData.MinSwapAmount}.");

                if (amount.Value > tokenData.MaxSwapAmount)
                    throw new BadRequestException(
                        $"Maximum swap amount for {token} on {network} is {tokenData.MaxSwapAmount}.");
            }

            return tokenData;
        }

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

        #endregion
    }
}


//public async Task AddTransactionToSwapAsync(AddTransactionToSwapUpdate update)
//{
//    if (update == null || string.IsNullOrWhiteSpace(update.SwapReference))
//        return;

//    var swap = await _swapRepository.AsQueryable()
//        .FirstOrDefaultAsync(q => q.SwapReference == update.SwapReference);

//    if (swap == null) return;

//    var tokenData = GetTokenWithAddressAndNetwork(update.TokenAddress, update.Network);
//    var tokenAmount = _blockChainService.ConvertFromWei(update.Amount, tokenData.PriceDecimalPlaces);

//    var transaction = new SwapTransaction
//    {
//        CreateMoment = DateTime.UtcNow,
//        Hash = update.Hash,
//        Network = update.Network,
//        Symbol = tokenData.Name,
//        Amount = tokenAmount,
//        Type = update.Type
//    };

//    var newState = update.Type switch
//    {
//        SwapTransactionType.Init => SwapState.Pending,
//        SwapTransactionType.Execute => SwapState.Completed,
//        SwapTransactionType.Failed => SwapState.Failed,
//        _ => swap.State
//    };

//    var filter = Builders<Swap>.Filter.Eq(x => x.SwapReference, update.SwapReference);

//    var updateDefinition = Builders<Swap>.Update
//        .Push(x => x.Transactions, transaction)
//        .Set(x => x.State, newState);


//    await _swapRepository.FindOneAndUpdateAsync(filter, updateDefinition);

//    if (update.Type == SwapTransactionType.Execute)
//    {
//        var amountFilter = Builders<Swap>.Filter.And(
//            filter,
//            Builders<Swap>.Filter.Lt(x => x.DestinationAmount, tokenAmount)
//        );

//        await _swapRepository.FindOneAndUpdateAsync(
//            amountFilter,
//            Builders<Swap>.Update.Set(x => x.DestinationAmount, tokenAmount)
//        );
//    }


//    string message = update.Type switch
//    {
//        SwapTransactionType.Init =>
//            $"Swap started: {swap.SourceAmount} {swap.SourceSymbol} → {swap.DestinationSymbol}.",

//        SwapTransactionType.Execute =>
//            $"Swap completed: You received {swap.DestinationAmount} {swap.DestinationSymbol}.",

//        SwapTransactionType.Failed =>
//            $"Swap failed: {swap.SourceSymbol} → {swap.DestinationSymbol}. Please try again.",

//        _ => "Swap status updated."
//    };

//    try
//    {
//        await _hubContext.Clients.Group(swap.WalletAddress)
//            .SendAsync("SwapMessage", message);
//    }
//    catch (Exception ex)
//    {
//        _logger.LogError(ex,
//            "Failed to send Swap notification for SwapReference {SwapReference}",
//            update.SwapReference);
//    }
//}
