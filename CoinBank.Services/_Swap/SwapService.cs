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

        public async Task<SwapCreatedResult> CreateSwapAsync(CreateSwapUpdate update, string walletAddress, string walletType, string publicKey)
        {
            var sourceNetwork = update.SourceNetwork.ToUpper();
            if (sourceNetwork != walletType) throw new BadRequestException($"please sign with {update.SourceNetwork} Wallet!");


            //var walletAddress = SpesifyWalletAddress(EVMwalletAddress, TronWalletAddress, update.SourceNetwork.ToUpper());

            ValidateDifferentTokens(update);
            var sourceTokenData = GetAndValidateSwappableToken(update.SourceNetwork, update.SourceSymbol);
            var destinationTokenData = GetAndValidateSwappableToken(update.DestinationNetwork, update.DestinationToken);
            await ValidateBalancesForSwapAsync(update, walletAddress);



            #region Source
            var swapReference = IdGenerartor.GenerateBytes32HexId();
            var srcEid = _blockChainService.MapNetworkToEid(update.SourceNetwork);
            var sourceSymbol = update.SourceSymbol.ToUpper();
            var sourceTokenAddress = sourceTokenData.Address;
            var sourceTokenPrice = await _priceService.GetOneTokenPriceForInternalUsageAsync(sourceSymbol);
            var sourceAmount = update.SourceTokenAmount;
            var sourceAmountInWei = _blockChainService.ConvertToWei(sourceAmount, sourceTokenData.PriceDecimalPlaces);
            var sourceWallet = walletAddress;
            #endregion

            #region Destination
            var destinationNetwork = update.DestinationNetwork.ToUpper();
            var destinationSymbol = update.DestinationToken.ToUpper();
            var destinationTokenAddress = destinationTokenData.Address;
            var destinationWallet = update.DestinationWallet;
            var dstEid = _blockChainService.MapNetworkToEid(update.DestinationNetwork);

            var destinationTokenPrice = await _priceService.GetOneTokenPriceForInternalUsageAsync(destinationSymbol);
            #endregion

            var swapPaths = update.Paths;
            SwapPathValidation(swapPaths, sourceTokenAddress, destinationTokenAddress);


            var (fee, feeToken) = await _blockChainService.SwapGetEstimatedFeeAsync(new _BlockChain.DTOs.Updates.GetSwapEstimatedFeeUpdate
            {
                SwapReference = swapReference,
                DstEid = dstEid,
                SourceNetwork = sourceNetwork,
                SourceTokenAddress = sourceTokenAddress,
                SourceAmoutInWei = sourceAmountInWei,
                DestinationNetwork = destinationNetwork,
                DestinationTokenAddress = destinationTokenAddress,
                DestinationWallet = destinationWallet,
                Paths = swapPaths,
            });

            var destinationTokenOutAmountInWei = await _blockChainService.SwapGetOutputAmountAsync(new _BlockChain.DTOs.Updates.SwapGetOutputAmount
            {
                DestinationTokenAddress = destinationTokenAddress,
                SourceTokenAddress = sourceTokenAddress,
                SourceAmountInWei = sourceAmountInWei,
                SrcEid = srcEid,
                DstEid = dstEid,
                Paths = swapPaths
            });

            var destinationContractBalance = await _blockChainService.GetLiquidityBalanceAsync(destinationSymbol, destinationNetwork);

            if (destinationContractBalance < destinationTokenOutAmountInWei)
            {
                throw new BadRequestException($"Insufficient liquidity for {destinationSymbol}.");
            }

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

                DstEid = dstEid,
                SrcEid = srcEid,

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


        private void SwapPathValidation(List<string> paths, string sourceTokenAddress, string destinationTokenAddress)
        {
            if (paths == null || paths.Count < 2)
                throw new BadRequestException("Invalid Paths!");

            List<string> usdtAddresses = ["0xdAC17F958D2ee523a2206206994597C13D831ec7", "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t"];
            var Bep20UsdtAddress = "0x55d398326f99059fF775485246999027B3197955";

            var firstPath = paths.First();
            if (usdtAddresses.Contains(firstPath))
            {
                firstPath = Bep20UsdtAddress;
            }

            var lastPath = paths.Last();
            if (usdtAddresses.Contains(lastPath))
            {
                lastPath = Bep20UsdtAddress;
            }

            if (!string.Equals(sourceTokenAddress, firstPath, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("Invalid Path Start!");

            if (!string.Equals(destinationTokenAddress, lastPath, StringComparison.OrdinalIgnoreCase))
                throw new BadRequestException("Invalid Path End!");

            var distinctCount = paths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (distinctCount != paths.Count)
                throw new BadRequestException("Duplicate Path found in path!");
        }

        //private string SpesifyWalletAddress(string EVMwalletAddress, string TromWalletAddress, string network)
        //{
        //    if (network == "BEP20")
        //    {
        //        if (EVMwalletAddress.IsNullOrEmpty()) throw new BadRequestException("Please sign with your BSC wallet");
        //        return EVMwalletAddress;
        //    }
        //    else if (network == "ERC20")
        //    {
        //        if (EVMwalletAddress.IsNullOrEmpty()) throw new BadRequestException("Please sign with your ETH wallet");
        //        return EVMwalletAddress;

        //    }
        //    else if (network == "TRC20")
        //    {
        //        if (TromWalletAddress.IsNullOrEmpty()) throw new BadRequestException("Please sign with your TRON wallet");
        //        return TromWalletAddress;

        //    }
        //    else throw new BadRequestException("Wrong network!");


        //}

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
                    (x.WalletAddress == walletAddress)
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
            {
                _logger.LogWarning("Invalid update payload.");
                return;
            }

            var tokenData = GetTokenWithAddressAndNetwork(update.TokenAddress, update.Network);
            if (tokenData == null)
            {
                _logger.LogWarning("Token not found. TokenAddress: {TokenAddress}, Network: {Network}",
                    update.TokenAddress, update.Network);
                return;
            }

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

            var filter = Builders<Swap>.Filter.And(
                Builders<Swap>.Filter.Eq(x => x.SwapReference, update.SwapReference),
                Builders<Swap>.Filter.Not(
                    Builders<Swap>.Filter.ElemMatch(x => x.Transactions, t => t.Type == update.Type)
                )
            );

            var updateDef = Builders<Swap>.Update.Push(x => x.Transactions, transaction);

            var options = new FindOneAndUpdateOptions<Swap>
            {
                ReturnDocument = ReturnDocument.After
            };

            var swap = await _swapRepository.FindOneAndUpdateWithOptionAsync(filter, updateDef, options);


            if (swap == null)
            {
                //var existingSwap = await _swapRepository.AsQueryable()
                //     .FirstOrDefaultAsync(x => x.SwapReference == update.SwapReference);

                //if (existingSwap == null)
                //{
                //    _logger.LogWarning(
                //        "Swap not found while adding transaction. SwapReference: {SwapReference}, Type: {Type}",
                //        update.SwapReference,
                //        update.Type
                //    );
                //    return;
                //}
                _logger.LogInformation(
                   "Duplicate transaction ignored. SwapReference: {SwapReference}, Type: {Type}",
                   update.SwapReference,
                   update.Type
               );
                return;
            }

            if (swap.RegisterHash == null)
            {
                var registerUpdateFilter = Builders<Swap>.Filter.And(
                    Builders<Swap>.Filter.Eq(x => x.Id, swap.Id),
                    Builders<Swap>.Filter.Eq(x => x.RegisterHash, null)
                );

                var registerUpdate = Builders<Swap>.Update
                    .Set(x => x.RegisterHash, update.Hash)
                    .Set(x => x.RegisterMoment, DateTime.UtcNow);

                await _swapRepository.FindOneAndUpdateAsync(
                    registerUpdateFilter,
                    registerUpdate);

                swap.RegisterHash = update.Hash;
                swap.RegisterMoment = DateTime.UtcNow;
            }
            await SyncSwapStateAsync(swap);

            var message = BuildSwapMessage(swap, update.Type);

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

        private async Task SyncSwapStateAsync(Swap swap)
        {
            //var swap = await _swapRepository.AsQueryable().FirstOrDefaultAsync(q => q.SwapReference == swapReference);

            if (swap == null)
            {
                _logger.LogWarning("Swap not found for sync. SwapReference: {SwapReference}", swap.SwapReference);
                return;
            }

            if (swap.Transactions == null || !swap.Transactions.Any())
                return;

            var hasFailed = swap.Transactions.Any(t => t.Type == SwapTransactionType.Failed);
            var hasExecute = swap.Transactions.Any(t => t.Type == SwapTransactionType.Execute);
            var hasInit = swap.Transactions.Any(t => t.Type == SwapTransactionType.Init);

            SwapState newState;

            if (hasFailed)
                newState = SwapState.Failed;
            else if (hasExecute)
                newState = SwapState.Completed;
            else if (hasInit)
                newState = SwapState.Pending;
            else
                newState = swap.State; // fallback


            var maxExecuteAmount = swap.Transactions
                .Where(t => t.Type == SwapTransactionType.Execute)
                .Select(t => t.Amount)
                .DefaultIfEmpty(0)
                .Max();

            var updates = new List<UpdateDefinition<Swap>>();

            //if(newState == SwapState.Pending || newState == SwapState.Completed )
            //{ }

            if (swap.State != newState)
            {
                updates.Add(Builders<Swap>.Update.Set(x => x.State, newState));
            }


            if (maxExecuteAmount > 0 && swap.DestinationAmount != maxExecuteAmount)
                updates.Add(Builders<Swap>.Update.Set(x => x.DestinationAmount, maxExecuteAmount));

            if (!updates.Any())
                return;

            var updateDef = Builders<Swap>.Update.Combine(updates);

            await _swapRepository.FindOneAndUpdateAsync(
                Builders<Swap>.Filter.Eq(x => x.SwapReference, swap.SwapReference),
                updateDef
            );
        }

        private string BuildSwapMessage(Swap swap, SwapTransactionType type)
        {
            return type switch
            {
                SwapTransactionType.Init =>
                    $"Swap started: {swap.SourceAmount} {swap.SourceSymbol} → {swap.DestinationSymbol}.",

                SwapTransactionType.Execute =>
                    $"Swap completed: You received {swap.DestinationAmount} {swap.DestinationSymbol}.",

                SwapTransactionType.Failed =>
                    $"Swap failed: {swap.SourceSymbol} → {swap.DestinationSymbol}. Please try again.",

                _ => "Swap status updated."
            };
        }


        //public async Task AddTransactionToSwapAsync(AddTransactionToSwapUpdate update)
        //{
        //    if (update == null || string.IsNullOrWhiteSpace(update.SwapReference))
        //        return;

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

        //    var baseFilter = Builders<Swap>.Filter.Eq(x => x.SwapReference, update.SwapReference);

        //    var newState = update.Type switch
        //    {
        //        SwapTransactionType.Init => SwapState.Pending,
        //        SwapTransactionType.Execute => SwapState.Completed,
        //        SwapTransactionType.Failed => SwapState.Failed,
        //        _ => (SwapState?)null
        //    };

        //    var updateBuilder = Builders<Swap>.Update
        //        .Push(x => x.Transactions, transaction);

        //    if (newState.HasValue)
        //        updateBuilder = updateBuilder.Set(x => x.State, newState.Value);

        //    FilterDefinition<Swap> finalFilter = baseFilter;

        //    if (update.Type == SwapTransactionType.Execute)
        //    {
        //        var amountFilter = Builders<Swap>.Filter.Lt(x => x.DestinationAmount, tokenAmount);
        //        finalFilter = Builders<Swap>.Filter.And(baseFilter, amountFilter);

        //        updateBuilder = updateBuilder.Set(x => x.DestinationAmount, tokenAmount);
        //    }

        //    var options = new FindOneAndUpdateOptions<Swap>
        //    {
        //        ReturnDocument = ReturnDocument.After
        //    };

        //    var updatedSwap = await _swapRepository.FindOneAndUpdateWithOptionAsync(finalFilter, updateBuilder, options);

        //    if (updatedSwap == null && update.Type == SwapTransactionType.Execute)
        //    {
        //        updatedSwap = await _swapRepository.FindOneAndUpdateWithOptionAsync(
        //            baseFilter,
        //            Builders<Swap>.Update
        //                .Push(x => x.Transactions, transaction)
        //                .Set(x => x.State, SwapState.Completed),
        //            options
        //        );
        //    }

        //    if (updatedSwap == null)
        //        return;

        //    var message = BuildSwapMessage(updatedSwap, update.Type);

        //    if (update.Type == SwapTransactionType.Execute)
        //    {
        //        await UpdateSingleTokenInStorageAsync(update.TokenAddress, update.Network);
        //    }

        //    try
        //    {
        //        await _hubContext.Clients.Group(updatedSwap.WalletAddress)
        //            .SendAsync("SwapMessage", message);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex,
        //            "Failed to send Swap notification for SwapReference {SwapReference}",
        //            update.SwapReference);
        //    }
        //}

        //public async Task ProcessSwapTransactionAsync(AddTransactionToSwapUpdate update)
        //{
        //    if (update == null || string.IsNullOrWhiteSpace(update.SwapReference))
        //    {
        //        _logger.LogWarning("Invalid update payload.");
        //        return;
        //    }

        //    var tokenData = GetTokenWithAddressAndNetwork(update.TokenAddress, update.Network);
        //    if (tokenData == null)
        //    {
        //        _logger.LogWarning("Token not found: {TokenAddress} - {Network}", update.TokenAddress, update.Network);
        //        return;
        //    }

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

        //    //ignore duplicate transaction
        //    var baseFilter = Builders<Swap>.Filter.And(
        //        Builders<Swap>.Filter.Eq(x => x.SwapReference, update.SwapReference),
        //        Builders<Swap>.Filter.Ne("Transactions.Hash", update.Hash)
        //    );

        //    var updateBuilder = Builders<Swap>.Update
        //        .Push(x => x.Transactions, transaction);


        //    var newState = update.Type switch
        //    {
        //        SwapTransactionType.Init => SwapState.Pending,
        //        SwapTransactionType.Execute => SwapState.Completed,
        //        SwapTransactionType.Failed => SwapState.Failed,
        //        _ => (SwapState?)null
        //    };

        //    if (newState.HasValue)
        //    {
        //        updateBuilder = updateBuilder.Set(x => x.State, newState.Value);
        //    }

        //    FilterDefinition<Swap> finalFilter = baseFilter;

        //    // فقط اگر Execute بود، مقدار destination رو فقط در صورت بزرگتر بودن آپدیت کن
        //    if (update.Type == SwapTransactionType.Execute)
        //    {
        //        var amountFilter = Builders<Swap>.Filter.Or(
        //            Builders<Swap>.Filter.Exists(x => x.DestinationAmount, false),
        //            Builders<Swap>.Filter.Lt(x => x.DestinationAmount, tokenAmount)
        //        );

        //        finalFilter = Builders<Swap>.Filter.And(baseFilter, amountFilter);

        //        updateBuilder = updateBuilder.Set(x => x.DestinationAmount, tokenAmount);
        //    }

        //    var options = new FindOneAndUpdateOptions<Swap>
        //    {
        //        ReturnDocument = ReturnDocument.After
        //    };

        //    var updatedSwap = await _swapRepository.FindOneAndUpdateWithOptionAsync(finalFilter, updateBuilder, options);

        //    // fallback برای Execute (مثلاً وقتی مقدار کوچکتر بوده)
        //    if (updatedSwap == null && update.Type == SwapTransactionType.Execute)
        //    {
        //        var fallbackUpdate = Builders<Swap>.Update
        //            .Push(x => x.Transactions, transaction)
        //            .Set(x => x.State, SwapState.Completed);

        //        updatedSwap = await _swapRepository.FindOneAndUpdateWithOptionAsync(
        //            Builders<Swap>.Filter.Eq(x => x.SwapReference, update.SwapReference),
        //            fallbackUpdate,
        //            options
        //        );
        //    }

        //    if (updatedSwap == null)
        //    {
        //        _logger.LogWarning("Swap not found or duplicate transaction ignored: {SwapReference}", update.SwapReference);
        //        return;
        //    }

        //    var message = BuildSwapMessage(updatedSwap, update.Type);

        //    if (update.Type == SwapTransactionType.Execute)
        //    {
        //        await UpdateSingleTokenInStorageAsync(update.TokenAddress, update.Network);
        //    }

        //    try
        //    {
        //        await _hubContext.Clients.Group(updatedSwap.WalletAddress)
        //            .SendAsync("SwapMessage", message);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex,
        //            "Failed to send Swap notification for SwapReference {SwapReference}",
        //            update.SwapReference);
        //    }
        //}


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
                throw new BadRequestException("User wallet is required");

            var sourceToken = _availableTokenDatas.FirstOrDefault(t =>
                t.Name == update.SourceSymbol && t.Network == update.SourceNetwork);

            var destinationToken = _availableTokenDatas.FirstOrDefault(t =>
                t.Name == update.DestinationToken && t.Network == update.DestinationNetwork);

            if (sourceToken == null)
                throw new BadRequestException("Source token not supported");

            if (destinationToken == null)
                throw new BadRequestException("Destination token not supported");

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
                throw new BadRequestException("Unsupported network for source token");
            }

            if (userBalance < update.SourceTokenAmount)
                throw new BadRequestException("Insufficient balance");


            var sourcePrice = await _priceService.GetOneTokenPriceForInternalUsageAsync(sourceToken.Name);
            var destinationPrice = await _priceService.GetOneTokenPriceForInternalUsageAsync(destinationToken.Name);

            if (sourcePrice <= 0 || destinationPrice <= 0)
                throw new BadRequestException("Price error");

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
                    throw new BadRequestException("Unsupported destination network");
                }

                if (!contractBalances.TryGetValue(destinationToken.Name, out contractBalance))
                    throw new BadRequestException("Destination token liquidity not found");


                _swapStorage.UpdateContractBalance(destinationToken.Address, contractBalance);

            }

            if (contractBalance < destinationAmount)
                throw new BadRequestException("Insufficient liquidity");
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
