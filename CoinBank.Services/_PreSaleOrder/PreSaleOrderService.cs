using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using CoinBank.Services._BlockChain;
using CoinBank.Services._BlockChain.DTOs.Settings;
using CoinBank.Services._BlockChain.DTOs.Updates;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._Common.Services;
using CoinBank.Services._PreSale;
using CoinBank.Services._PreSale.DTOs.Storages;
using CoinBank.Services._PreSaleOrder.DTOs.Results;
using CoinBank.Services._PreSaleOrder.DTOs.Updates;
using CoinBank.Services._Transaction._Hub;
using Microsoft.AspNetCore.SignalR;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Nethereum.ABI.EIP712;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using System.Numerics;
using Utilities.Exceptions.Common;
using Utilities.Utilities;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._PreSaleOrder
{
    public class PreSaleOrderService(
        IPreSaleService _preSaleService,
        AvailableTokensSettings _availableTokenData,
        IPreSaleOrderRepository _preSaleOrderRepository,
        BlockChainSettings _blockChainSettings,
        ILogger<PreSaleOrderService> _logger,
        PreSaleStorage _preSaleStorage,
        IHubContext<WalletNotifyHub> _hubContext,
        IBlockChainService _blockChainService) : IPreSaleOrderService, IScopedDependency
    {

        /// <summary>
        /// use for create pre sale order 
        /// </summary>
        /// <param name="update"></param>
        /// <param name="publicKey"></param>
        /// <param name="evmWalletAddress"></param>
        /// <returns></returns>
        public async Task<PreSaleOrderResult> CreatePreSaleOrderAsync(CreatePreSaleOrderUpdate update, string publicKey, string evmWalletAddress)
        {
            var presale = await _preSaleService
                .GetPreSaleDataByReferenceForInternalUsageAsync(update.PreSaleReference);

            if (presale.StartSellingAt > DateTime.UtcNow)
                throw new BadRequestException($"Pre-sale starts at {presale.StartSellingAt}");

            if (presale.EndSellingAt < DateTime.UtcNow)
                throw new BadRequestException("Pre-sale has ended");

            var userOrders = await GetUserActiveOrders(presale.PreSaleReference, publicKey);

            ValidateUserOrderCount(userOrders);

            var userTotalAmount = userOrders.Sum(x => x.ReceivingTokenAmount);
            await ValidateOrderAmount(presale, update.TokenAmount, userTotalAmount);
            await ValidateUserAndContractBalance(evmWalletAddress, presale.Price, update.TokenAmount,presale.Symbol);

            var tokenData = ValidateToken(presale.Symbol);

            var receivingTokenAmount = update.TokenAmount;
            var receivingTokenAmountInWei = _blockChainService.ConvertToWei(update.TokenAmount, tokenData.PriceDecimalPlaces);
            var paymetTokenAmount = receivingTokenAmount * presale.Price;
            var paymetTokenAmountInWei = _blockChainService.ConvertToWei(paymetTokenAmount);

            var newOrder = new PreSaleOrder
            {
                PreSaleOrderReference = IdGenerartor.GenerateBytes32HexId(),
                PreSaleReference = presale.PreSaleReference,
                Symbol = presale.Symbol,
                LogoUrl = presale.LogoUrl,
                Name = presale.Name,
                PaymentToken = "RZUSD",
                RegisterHash = null,
                RegisterMoment = null,
                UserPublicKey = publicKey,
                WalletAddress = evmWalletAddress,
                State = PreSaleOrderState.NotRegistered,
                TokenPreSalePrice = presale.Price,
                PaymentTokenAmount = paymetTokenAmount,
                PaymentTokenAmountInWei = paymetTokenAmountInWei.ToString(),
                ReceivingTokenAmount = receivingTokenAmount,
                ReceivingTokenAmountInWei = receivingTokenAmountInWei.ToString(),
                ReleaseSchedule = MapReleaseSchedule(presale.ReleaseSchedule)
            };


            var signatureExpirer = DateTime.UtcNow.AddMinutes(10);
            var signature = await GeneratePurchaseSignature(newOrder.PreSaleReference, newOrder.PreSaleOrderReference,
                newOrder.WalletAddress, receivingTokenAmountInWei, newOrder.PaymentToken, paymetTokenAmountInWei, signatureExpirer);

            newOrder.Signature = signature;
            newOrder.SignatureExpire = signatureExpirer;

            await _preSaleOrderRepository.InsertOneAsync(newOrder);

            return ConvertToResult(newOrder);
        }

       
        /// <summary>
        /// use for convert hex to byte32
        /// </summary>
        /// <param name="hex"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static byte[] HexToByteArray32(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                throw new ArgumentException("Hex string is null or empty");

            var bytes = Nethereum.Hex.HexConvertors.Extensions.HexByteConvertorExtensions.HexToByteArray(hex);

            if (bytes.Length > 32)
                throw new ArgumentException("Hex string is too long for bytes32");

            var padded = new byte[32];
            Array.Copy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);

            return padded;
        }


        /// <summary>
        /// use for get user preSale history
        /// </summary>
        /// <param name="update"></param>
        /// <param name="publicKey"></param>
        /// <param name="evmWalletAddress"></param>
        /// <returns></returns>
        public async Task<PreSaleOrderListResult> GetPreSaleOrderHistoryAsync(GetPreSaleOrderHistoryUpdate update, string publicKey, string evmWalletAddress)
        {

            var query = _preSaleOrderRepository.AsQueryable();

            if (update.Symbol != null && update.Symbol.HasValue())
            {
                query = query.Where(q => q.Symbol == update.Symbol.ToUpper());
            }

            if (string.IsNullOrWhiteSpace(publicKey)) throw new BadRequestException("Access denied!");

            if (publicKey == "guess")
            {
                query = query.Where(x =>
                    x.WalletAddress == evmWalletAddress &&
                    x.State != PreSaleOrderState.NotRegistered);
            }
            else
            {
                query = query.Where(x =>
                    (x.UserPublicKey == publicKey) &&
                    x.State != PreSaleOrderState.NotRegistered);
            }

            var totalCount = await query.CountAsync();

            var page = update.Pagination?.Page ?? 1;
            var size = update.Pagination?.Size ?? 25;

            var data = await query
                .OrderByDescending(x => x.RegisterMoment)
                .Skip((page - 1) * size)
                .Take(size)
                .ToListAsync();

            var result = new PreSaleOrderListResult
            {
                TotalCount = totalCount,
                PageCount = (int)Math.Ceiling((double)totalCount / size),
                Data = data.Select(ConvertToResult).ToList()
            };

            return result;
        }


        /// <summary>
        /// use for get pre sale order wallet stats
        /// </summary>
        /// <param name="update"></param>
        /// <param name="publicKey"></param>
        /// <param name="evmWalletAddress"></param>
        /// <returns></returns>
        public async Task<List<PreSaleOrderWalletStatsResult>> GetWalletStatsAsync(GetPreSaleOrderWalletStatsUpdate update, string publicKey, string evmWalletAddress)
        {
            var query = _preSaleOrderRepository.AsQueryable()
                .Where(x => x.State != PreSaleOrderState.NotRegistered);

            if (string.IsNullOrWhiteSpace(publicKey) || publicKey == "guess")
            {
                query = query.Where(x => x.WalletAddress == evmWalletAddress);
            }
            else
            {
                query = query.Where(x => x.UserPublicKey == publicKey);
            }

            if (update.ShouldGrouped)
            {
                var grouped = await query
                    .GroupBy(x => x.Symbol)
                    .Select(g => new PreSaleOrderWalletStatsResult
                    {
                        Symbol = g.Key,

                        Name = g.Select(x => x.Name).FirstOrDefault(),
                        LogoUrl = g.Select(x => x.LogoUrl).FirstOrDefault(),

                        TokenPreSalePrice = g.Select(x => x.TokenPreSalePrice).FirstOrDefault(),

                        ReleaseSchedule = g
                            .Where(x => x.ReleaseSchedule != null)
                            .Select(x => x.ReleaseSchedule)
                            .FirstOrDefault(),

                        ReceivingTokenAmount = g.Sum(x => x.ReceivingTokenAmount),
                        TotalPaymentToken = g.Sum(x => x.PaymentTokenAmount),
                        OrderCount = g.Count(),
                        PreSaleOrderReference = null
                    })
                    .ToListAsync();

                return grouped;
            }


            var list = await query
                .Select(x => new PreSaleOrderWalletStatsResult
                {
                    Symbol = x.Symbol,
                    Name = x.Name,
                    LogoUrl = x.LogoUrl,
                    TokenPreSalePrice = x.TokenPreSalePrice,
                    ReleaseSchedule = x.ReleaseSchedule,
                    ReceivingTokenAmount = x.ReceivingTokenAmount,
                    TotalPaymentToken = x.PaymentTokenAmount,
                    PreSaleOrderReference = x.PreSaleOrderReference,
                    OrderCount = 1
                })
                .ToListAsync();

            return list;
        }


        /// <summary>
        /// use for get one pre sale order detail with transactions(releases)
        /// </summary>
        /// <param name="update"></param>
        /// <param name="publicKey"></param>
        /// <param name="evmWalletAddress"></param>
        /// <returns></returns>
        /// <exception cref="NotFoundException"></exception>
        public async Task<PreSaleOrderDetailResult> GetOnePreSaleOrderDetailAsync(GetOnePreSaleOrderDetailUpdate update, string publicKey, string evmWalletAddress)
        {
            var query = _preSaleOrderRepository.AsQueryable().Where(x =>
                    x.PreSaleOrderReference == update.PreSaleOrderReference);

            if (string.IsNullOrWhiteSpace(publicKey) || publicKey == "guess")
            {
                query = query.Where(x => x.WalletAddress == evmWalletAddress);
            }
            else
            {
                query = query.Where(x => x.UserPublicKey == publicKey);
            }

            var order = await query.FirstOrDefaultAsync() ?? throw new NotFoundException("PreSale order not found!");



            decimal totalReleased = order.ReleaseSchedule.Where(q => q.RegisterHash != null)
                .Sum(x => x.CliamedAmount ?? 0);

            decimal remainForRelease = order.ReceivingTokenAmount - totalReleased;

            return new PreSaleOrderDetailResult
            {
                PreSaleOrderReference = order.PreSaleOrderReference,
                PreSaleReference = order.PreSaleReference,
                WalletAddress = order.WalletAddress,
                Name = order.Name,
                Symbol = order.Symbol,
                LogoUrl = order.LogoUrl,
                TokenPreSalePrice = order.TokenPreSalePrice,
                PaymentToken = order.PaymentToken,
                PaymentTokenAmountInWei = order.PaymentTokenAmountInWei,
                PaymentTokenAmount = order.PaymentTokenAmount,
                ReceivingTokenAmountInWei = order.ReceivingTokenAmountInWei,
                ReceivingTokenAmount = order.ReceivingTokenAmount,
                ReleaseSchedule = order.ReleaseSchedule,
                State = order.State,
                Signature = order.Signature,
                SignatureExpire = order.SignatureExpire,
                ModifiedMoment = order.ModifiedMoment,
                CreatedMoment = order.CreatedMoment,

                RemainReleaseTokenAmount = remainForRelease,
                TotalReleasedTokenAmount = totalReleased,
            };
        }


        /// <summary>
        /// use for active a order from events
        /// </summary>
        /// <param name="preSaleOrderReference"></param>
        /// <param name="registerHash"></param>
        /// <returns></returns>
        public async Task ActivatePreSaleOrderForInternalUsageAsync(string preSaleOrderReference, string registerHash)
        {
            var order = await _preSaleOrderRepository.AsQueryable()
                .FirstOrDefaultAsync(q => q.PreSaleOrderReference.ToLower() == preSaleOrderReference.ToLower() && q.State == PreSaleOrderState.NotRegistered);

            if (order != null)
            {
                order.RegisterHash = registerHash;
                order.RegisterMoment = DateTime.UtcNow;
                order.State = PreSaleOrderState.InProgress;

                await _preSaleOrderRepository.ReplaceOneAsync(order);
                await _preSaleService.SyncPreSaleToStorageAsync(order.PreSaleReference);
            }
        }


        /// <summary>
        /// search for release for call to claim
        /// </summary>
        /// <returns></returns>
        public async Task ProcessReleaseOrderAsync()
        {
            try
            {
                var now = DateTime.UtcNow;

                var query = _preSaleOrderRepository.AsQueryable()
                    .Where(x => x.State == PreSaleOrderState.InProgress)
                    .Where(x => x.ReleaseSchedule.Any(r =>
                        r.ReleaseDate <= now &&
                        r.RegisterMoment == null
                    ));

                var order = await query.FirstOrDefaultAsync();
                if (order != null) await ProcessNextReleaseAsync(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching ready release order");
                throw;
            }
        }

      
        /// <summary>
        /// get one for internal usage
        /// do not check the null
        /// </summary>
        /// <param name="preSaleOrderRef"></param>
        /// <returns></returns>
        public async Task<PreSaleOrder> GetOneByReferenceForInternalUsageAsync(string preSaleOrderRef)
        {
            return await _preSaleOrderRepository.AsQueryable().FirstOrDefaultAsync(q => q.PreSaleOrderReference == preSaleOrderRef);
        }


        /// <summary>
        /// use for update a step that was registered
        /// </summary>
        /// <param name="preSaleOrderRef"></param>
        /// <param name="txHash"></param>
        /// <param name="claimedAmount"></param>
        /// <returns></returns>
        public async Task UpdatePreSaleReleaseStepForAddTransactionAsync(string preSaleOrderRef, string txHash, string claimedAmount)
        {
            var order = await _preSaleOrderRepository
                .AsQueryable()
                .FirstOrDefaultAsync(q => q.PreSaleOrderReference == preSaleOrderRef);

            if (order == null)
                return;

            if (order.ReleaseSchedule == null || !order.ReleaseSchedule.Any())
                return;

            var step = order.ReleaseSchedule
                .Where(x => x.RegisterMoment != null)
                .OrderByDescending(x => x.ReleaseDate)
                .FirstOrDefault();

            if (step == null)
                return;


            var bigAmount = BigInteger.Parse(claimedAmount);
            var token = ValidateToken(order.Symbol);
            var convertedAmount = _blockChainService.ConvertFromWei(bigAmount, token.PriceDecimalPlaces);

            //step.TxHash = txHash;
            //step.TransactionMoment = DateTime.UtcNow;
            step.CliamedAmount = convertedAmount;

            await _preSaleOrderRepository.ReplaceOneAsync(order);
        }


        /// <summary>
        /// use for remove old NotRegistered PreSale Orders
        /// </summary>
        /// <returns></returns>
        public async Task RemoveNotRegisteredPreSaleOrderAsync()
        {
            var oneWeekAgo = DateTime.UtcNow.AddDays(-7);

            var query = _preSaleOrderRepository.AsQueryable();

            var ordersToDelete = await query
                .Where(x =>
                    x.RegisterHash == null &&
                    x.State == PreSaleOrderState.NotRegistered &&
                    x.CreatedMoment <= oneWeekAgo)
                .ToListAsync();

            if (ordersToDelete == null || ordersToDelete.Count == 0)
                return;

            var ids = ordersToDelete.Select(x => x.Id).ToList();

            await _preSaleOrderRepository.DeleteManyAsync(x => ids.Contains(x.Id));
        }


        #region Privates

        /// <summary>
        /// use for generate signature
        /// </summary>
        /// <param name="preSaleRef"></param>
        /// <param name="preSaleOrderRef"></param>
        /// <param name="walletAddress"></param>
        /// <param name="receiveAmountWei"></param>
        /// <param name="paymentToken"></param>
        /// <param name="paymentAmountWei"></param>
        /// <param name="signatureExpire"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private async Task<string> GeneratePurchaseSignature(string preSaleRef, string preSaleOrderRef,
            string walletAddress, BigInteger receiveAmountWei, string paymentToken, BigInteger paymentAmountWei, DateTime signatureExpire)
        {

            var tokenRegistry = new Dictionary<string, string>
            {
                ["RZUSD"] = "0xC4A1cc5cA8955a4650BDC109bddf110E33a1e344",
                ["USDT"] = "0x55d398326f99059fF775485246999027B3197955" // BSC USDT (official)
            };

            if (string.IsNullOrWhiteSpace(paymentToken) ||
                !tokenRegistry.ContainsKey(paymentToken))
                throw new Exception("Invalid PaidToken");

            var paymentTokenAddress = tokenRegistry[paymentToken];
            var nonce = await _blockChainService.PreSaleOrderGetNonceAsync(walletAddress, preSaleRef);
            var deadline = new DateTimeOffset(signatureExpire).ToUnixTimeSeconds();

            var domain = new Nethereum.ABI.EIP712.Domain
            {
                Name = "CoinBankPresale",
                Version = "1",
                ChainId = _blockChainSettings.BEP20ChainId,
                VerifyingContract = _blockChainSettings.PreSaleContractAddress
            };


            var typedData = new TypedData<Nethereum.ABI.EIP712.Domain>
            {
                Domain = domain,
                Types = new Dictionary<string, MemberDescription[]>
                {
                    ["EIP712Domain"] = new[]
                    {
                        new MemberDescription { Name = "name", Type = "string" },
                        new MemberDescription { Name = "version", Type = "string" },
                        new MemberDescription { Name = "chainId", Type = "uint256" },
                        new MemberDescription { Name = "verifyingContract", Type = "address" }
                    },
                    ["Purchase"] = new[]
                    {
                        new MemberDescription { Name = "saleId", Type = "bytes32" },
                        new MemberDescription { Name = "orderId", Type = "bytes32" },
                        new MemberDescription { Name = "buyer", Type = "address" },
                        new MemberDescription { Name = "receiveAmount", Type = "uint256" },
                        new MemberDescription { Name = "paymentToken", Type = "address" },
                        new MemberDescription { Name = "paymentAmount", Type = "uint256" },
                        new MemberDescription { Name = "nonce", Type = "uint256" },
                        new MemberDescription { Name = "deadline", Type = "uint256" }
                    }
                },

                PrimaryType = "Purchase",

                Message = new[]
                {
                    new MemberValue { TypeName = "bytes32", Value = HexToByteArray32(preSaleRef) },
                    new MemberValue { TypeName = "bytes32", Value = HexToByteArray32(preSaleOrderRef)},
                    new MemberValue { TypeName = "address", Value = walletAddress },
                    new MemberValue { TypeName = "uint256", Value = receiveAmountWei },
                    new MemberValue { TypeName = "address", Value = paymentTokenAddress },
                    new MemberValue { TypeName = "uint256", Value = paymentAmountWei },
                    new MemberValue { TypeName = "uint256", Value = nonce },
                    new MemberValue { TypeName = "uint256", Value = (ulong)deadline }
                }
            };


            var signer = new Eip712TypedDataSigner();
            var privateKey = _blockChainSettings.PrivateKey;

            return signer.SignTypedDataV4(typedData, new EthECKey(privateKey));
        }


        /// <summary>
        /// convertor method
        /// </summary>
        /// <param name="preSaleOrder"></param>
        /// <returns></returns>
        private PreSaleOrderResult ConvertToResult(PreSaleOrder preSaleOrder)
        {
            return new PreSaleOrderResult
            {
                PreSaleOrderReference = preSaleOrder.PreSaleOrderReference,
                PreSaleReference = preSaleOrder.PreSaleReference,
                WalletAddress = preSaleOrder.WalletAddress,
                Name = preSaleOrder.Name,
                Symbol = preSaleOrder.Symbol,
                LogoUrl = preSaleOrder.LogoUrl,
                TokenPreSalePrice = preSaleOrder.TokenPreSalePrice,
                PaymentToken = preSaleOrder.PaymentToken,
                PaymentTokenAmountInWei = preSaleOrder.PaymentTokenAmountInWei,
                PaymentTokenAmount = preSaleOrder.PaymentTokenAmount,
                ReceivingTokenAmountInWei = preSaleOrder.ReceivingTokenAmountInWei,
                ReceivingTokenAmount = preSaleOrder.ReceivingTokenAmount,
                ReleaseSchedule = preSaleOrder.ReleaseSchedule,
                State = preSaleOrder.State,
                Signature = preSaleOrder.Signature,
                SignatureExpire = preSaleOrder.SignatureExpire,
                ModifiedMoment = preSaleOrder.ModifiedMoment,
                CreatedMoment = preSaleOrder.CreatedMoment
            };
        }


        /// <summary>
        /// use for validate user balance
        /// </summary>
        /// <param name="wallet"></param>
        /// <param name="price"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
        /// <exception cref="BadRequestException"></exception>
        private async Task ValidateUserAndContractBalance(string wallet, decimal price, decimal amount,string symbol)
        {
            var balance = await _blockChainService
                .GetBEP20WalletAddressSingleTokenBalanceAsync(wallet, "RZUSD");

            var required = price * amount;

            if (balance < required)
                throw new BadRequestException("Insufficient RZUSD balance!");

            var preSale = _preSaleStorage.Values
                 .FirstOrDefault(x => x.Symbol == symbol);

            if (preSale == null)
                throw new BadRequestException("PreSale not found!");

            decimal contractBalance = preSale.ContractBalance;

            if (contractBalance < amount)
            {
                contractBalance = await _blockChainService.GetPreSaleContractSingleBalanceAsync(symbol);

                _preSaleStorage.UpdateContractBalance(symbol, contractBalance);

                if (contractBalance < amount)
                    throw new BadRequestException("Insufficient contract token balance!");
            }
        }


        /// <summary>
        /// use for get user active orders
        /// </summary>
        /// <param name="preSaleReference"></param>
        /// <param name="publicKey"></param>
        /// <returns></returns>
        private async Task<List<PreSaleOrder>> GetUserActiveOrders(string preSaleReference, string publicKey)
        {
            return await _preSaleOrderRepository.AsQueryable()
                .Where(x => x.PreSaleReference == preSaleReference &&
                            x.UserPublicKey == publicKey &&
                           (x.State == PreSaleOrderState.InProgress || x.State == PreSaleOrderState.Completed))
                .ToListAsync() ?? [];
        }


        /// <summary>
        /// use for validate user order count for each pre-sale ref
        /// </summary>
        /// <param name="orders"></param>
        /// <exception cref="BadRequestException"></exception>
        private void ValidateUserOrderCount(List<PreSaleOrder> orders)
        {
            if (orders.Count >= 5)
                throw new BadRequestException("maximum order for each token is 5");
        }


        /// <summary>
        /// use for get total sold amount in all orders
        /// </summary>
        /// <param name="preSaleReference"></param>
        /// <returns></returns>
        private async Task<decimal> GetTotalSoldAmount(string preSaleReference)
        {
            return await _preSaleOrderRepository.AsQueryable()
                .Where(x => x.PreSaleReference == preSaleReference &&
                       (x.State == PreSaleOrderState.InProgress || x.State == PreSaleOrderState.Completed))
                .SumAsync(x => (decimal?)x.ReceivingTokenAmount) ?? 0;
        }
       
        
        /// <summary>
        /// use for check amount and quantity
        /// </summary>
        /// <param name="preSale"></param>
        /// <param name="requestAmount"></param>
        /// <param name="userTotalAmount"></param>
        /// <returns></returns>
        /// <exception cref="BadRequestException"></exception>
        private async Task ValidateOrderAmount(PreSale preSale, decimal requestAmount, decimal userTotalAmount)
        {
            // min/max per order
            if (requestAmount < preSale.MinPerOrder)
                throw new BadRequestException($"Minimum amount is {preSale.MinPerOrder} {preSale.Symbol}");

            if (requestAmount > preSale.MaxPerOrder)
                throw new BadRequestException($"Maximum amount is {preSale.MaxPerOrder} {preSale.Symbol}");

            // user remaining quota
            var userRemain = preSale.MaxPerOrder - userTotalAmount;
            if (requestAmount > userRemain)
                throw new BadRequestException($"Your remaining quota is {userRemain} {preSale.Symbol}");


            var totalSold = await GetTotalSoldAmount(preSale.PreSaleReference);

            // total supply check
            var remainingSupply = preSale.TotalSupply - totalSold;

            if (remainingSupply <= 0)
                throw new BadRequestException("PreSale is sold out");

            if (requestAmount > remainingSupply)
                throw new BadRequestException($"Remaining total supply is {remainingSupply} {preSale.Symbol}");
        }


        /// <summary>
        /// use for validate token existing and return token data
        /// </summary>
        /// <param name="tokenName"></param>
        /// <returns></returns>
        /// <exception cref="BadRequestException"></exception>
        private AvailableTokenData ValidateToken(string tokenName)
        {

            if (tokenName == null)
                throw new BadRequestException($"Unsupported token name! {tokenName}");

            var tokenData = _availableTokenData.FirstOrDefault(q => q.Name.Equals(tokenName, StringComparison.OrdinalIgnoreCase))
                ?? throw new BadRequestException($"Unsupported token name! {tokenName}");
            return tokenData;
        }


        /// <summary>
        /// convertor
        /// </summary>
        /// <param name="schedule"></param>
        /// <returns></returns>
        private List<PreSaleOrderReleaseStep> MapReleaseSchedule(List<PreSaleReleaseStep> schedule)
        {
            if (schedule == null || !schedule.Any())
                return new List<PreSaleOrderReleaseStep>();

            return schedule
                .OrderBy(x => x.ReleaseDate)
                .Select(x => new PreSaleOrderReleaseStep
                {
                    ReleaseDate = x.ReleaseDate,
                    Percentage = x.Percentage,
                    RegisterMoment = null,
                    RegisterHash = null
                })
                .ToList();
        }


        /// <summary>
        /// notify blockchain for claim a release of order
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private async Task<string> ProcessNextReleaseAsync(PreSaleOrder order)
        {
            try
            {
                var now = DateTime.UtcNow;

                var step = order.ReleaseSchedule
                    .Where(x => x.ReleaseDate <= now && x.RegisterMoment == null)
                    .OrderBy(x => x.ReleaseDate)
                    .FirstOrDefault();

                if (step == null)
                {
                    _logger.LogWarning("Order found but no valid step to process");
                    return null;
                }

                var txHash = await _blockChainService.PreSaleOrderClaimTokensByOperatorAsync(
                    order.PreSaleReference,
                    order.PreSaleOrderReference
                );

                if (string.IsNullOrWhiteSpace(txHash))
                {
                    _logger.LogError("Blockchain returned empty txHash");
                    return null;
                }

                step.RegisterHash = txHash;
                step.RegisterMoment = now;
                step.CliamedAmount = (order.ReceivingTokenAmount * step.Percentage) / 100;

                if (order.ReleaseSchedule.All(x => x.RegisterMoment != null))
                {
                    order.State = PreSaleOrderState.Completed;
                }

                await _preSaleOrderRepository.ReplaceOneAsync(order);

                _logger.LogInformation(
                    "Step processed. OrderId: {OrderId}, TxHash: {TxHash}",
                    order.PreSaleOrderReference,
                    txHash
                );

                try
                {
                    var shortHash = txHash[..10];
                    await _hubContext.Clients.Group(order.WalletAddress)
                        .SendAsync("PreSaleMessage", $"Your claim  {shortHash}... was successful.");
                }
                catch (Exception)
                {
                    _logger.LogError("Failed to send PreSaleReleaseClaimed notification for txhash {txhash}", txHash);
                }

             

                return txHash;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing release");
                throw;
            }
        }


        #endregion

    }
}






///// <summary>
///// this method is for preSale service 
///// </summary>
///// <param name="preSaleReference"></param>
///// <returns></returns>
//public async Task MakeCompeletePreSaleOrderStateByPreSaleReferenceAsync(string preSaleReference)
//{
//    var now = DateTime.UtcNow;
//    var builder = Builders<PreSaleOrder>.Filter;

//    var filter = builder.And(
//        builder.Eq(x => x.PreSaleReference, preSaleReference),
//        builder.Eq(x => x.State, PreSaleOrderState.InProgress)
//    );

//    var update = Builders<PreSaleOrder>.Update
//        .Set(x => x.State, PreSaleOrderState.Completed);

//    await _preSaleOrderRepository.UpdateManyAsync(filter, update);
//}

//public static byte[] HexToBytes32(string hex)
//{
//    if (string.IsNullOrEmpty(hex))
//        throw new ArgumentException("Hex is null");

//    var bytes = Nethereum.Hex.HexConvertors.Extensions.HexByteConvertorExtensions
//        .HexToByteArray(hex);

//    if (bytes.Length != 32)
//        throw new ArgumentException("Invalid bytes32 length");

//    return bytes;