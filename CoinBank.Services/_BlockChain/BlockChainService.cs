using CoinBank.Domain.Collections;
using CoinBank.Services._BlockChain.DTOs.Settings;
using CoinBank.Services._BlockChain.DTOs.Updates;
using CoinBank.Services._Common.DTOs.Settings;
using CoinBank.Services._MultiCallService;
using CoinBank.Services._MultiCallService.DTOs;
using Microsoft.Extensions.Logging;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.ABI.Model;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using System.Numerics;
using System.Text.Json;
using Utilities.Exceptions.Common;
using static Utilities.Constants.RegisterMode;


namespace CoinBank.Services._BlockChain
{
    public class BlockChainService : IBlockChainService, ISingletonDependency
    {
        private const string PreSaleContractAbi = TokenForwardSaleAbi.PreSaleAbi;
        private const string ERC20Abi = TokenForwardSaleAbi.ERC20Abi;
        private const string SwapAbi = TokenForwardSaleAbi.SwapAbi;
        private const string StakeAbi = TokenForwardSaleAbi.StakeAbi;

        private readonly BlockChainSettings _settings;
        private readonly ILogger<BlockChainService> _logger;
        private readonly IMultiCallService _multicallService;
        private readonly AvailableTokensSettings _availableTokenData;
        private readonly Web3 _bep20Web3;
        private readonly Web3 _erc20Web3;
        private readonly Web3 _trc20Web3;
        private readonly Account _bep20Account;
        private readonly Contract _contract;


        public BlockChainService(BlockChainSettings settings,
            ILogger<BlockChainService> logger,
            IMultiCallService multiCallService,
            AvailableTokensSettings availableTokenData)
        {
            _settings = settings;
            _logger = logger;
            _multicallService = multiCallService;
            _availableTokenData = availableTokenData;
            if (string.IsNullOrEmpty(_settings.PrivateKey))
                throw new InvalidOperationException("Blockchain private key is not configured.");

            //_account = new Account(_settings.PrivateKey, _settings.ChainId);
            //_web3 = new Web3(_settings.RpcUrl2);

            _bep20Account = new Account(_settings.PrivateKey, _settings.BEP20ChainId);
            _bep20Web3 = new Web3(_bep20Account, _settings.BEP20RpcUrl);
            _bep20Web3.TransactionManager.UseLegacyAsDefault = true;
            _erc20Web3 = new Web3(_settings.ERC20RpcUrl);
            _trc20Web3 = new Web3(_settings.TRC20RpcUrl);

        }


        #region PreSale Methods

        public async Task<string> PreSaleOrderClaimTokensByOperatorAsync(string presaleId, string orderId)
        {
            if (string.IsNullOrEmpty(presaleId))
                throw new BadRequestException("PresaleId is required.");

            if (string.IsNullOrEmpty(orderId))
                throw new BadRequestException("OrderId is required.");

            try
            {
                var contract = _bep20Web3.Eth.GetContract(PreSaleContractAbi, _settings.PreSaleContractAddress);
                var function = contract.GetFunction("claimTokensByOperator");

                var presaleIdBytes = HexToByteArray32(presaleId);
                var orderIdBytes = HexToByteArray32(orderId);

                var gasPrice = await GetOptimalGasPriceAsync();
                var gas = new Nethereum.Hex.HexTypes.HexBigInteger(
                    _settings.GetDefaultGasLimit());

                var receipt = await function.SendTransactionAndWaitForReceiptAsync(
                    from: _bep20Account.Address,
                    gas: gas,
                    gasPrice: new Nethereum.Hex.HexTypes.HexBigInteger(gasPrice),
                    value: new Nethereum.Hex.HexTypes.HexBigInteger(0),
                    functionInput: new object[]
                    {
                        presaleIdBytes,
                        orderIdBytes
                    }
                );

                if (receipt.Status.Value == 1)
                {
                    _logger.LogInformation(
                        "ClaimTokensByOperator successful. TxHash: {TxHash}",
                        receipt.TransactionHash);

                    return receipt.TransactionHash;
                }

                _logger.LogError(
                    "ClaimTokensByOperator failed (reverted). TxHash: {TxHash}",
                    receipt.TransactionHash);

                return null;
            }
            catch (SmartContractRevertException revertEx)
            {
                _logger.LogError(
                    revertEx,
                    "Contract revert in ClaimTokensByOperator: {Message}",
                    revertEx.Message);

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in ClaimTokensByOperator.");
                return null;
            }
        }

        public async Task<string> PreSaleConfigureAsync(PreSale preSale)
        {
            if (preSale == null)
                throw new BadRequestException("PreSale is null.");

            if (string.IsNullOrEmpty(preSale.PreSaleReference))
                throw new BadRequestException("PreSaleReference is required.");

            var token = _availableTokenData
                .FirstOrDefault(x => x.Name == preSale.Symbol);

            if (token == null)
                throw new BadRequestException($"Token config not found for symbol: {preSale.Symbol}");

            try
            {
                var contract = _bep20Web3.Eth.GetContract(PreSaleContractAbi, _settings.PreSaleContractAddress);
                var function = contract.GetFunction("configurePresale");

                var saleIdBytes = HexToByteArray32(preSale.PreSaleReference);
                var decimals = token.PriceDecimalPlaces;

                var allocationWei = ConvertToWei(preSale.TotalSupply, decimals);
                var maxPerWalletWei = ConvertToWei(preSale.MaxPerOrder, decimals);

                var startUnix = new DateTimeOffset(NormalizeToUtc(preSale.StartSellingAt))
                    .ToUnixTimeSeconds();

                var endUnix = new DateTimeOffset(NormalizeToUtc(preSale.EndSellingAt))
                    .ToUnixTimeSeconds();

                var vestingArray = MapVestingData(preSale.ReleaseSchedule);

                var gasPrice = await GetOptimalGasPriceAsync();
                var gas = new Nethereum.Hex.HexTypes.HexBigInteger(
                    _settings.GetDefaultGasLimit());

                var receipt = await function.SendTransactionAndWaitForReceiptAsync(
                    from: _bep20Account.Address,
                    gas: gas,
                    gasPrice: new Nethereum.Hex.HexTypes.HexBigInteger(gasPrice),
                    value: new Nethereum.Hex.HexTypes.HexBigInteger(0),
                    functionInput: new object[]
                    {
                        saleIdBytes,
                        token.Address,
                        allocationWei,
                        maxPerWalletWei,
                        startUnix,
                        endUnix,
                        vestingArray
                    }
                );

                if (receipt.Status.Value == 1)
                {
                    _logger.LogInformation(
                        "ConfigurePresale successful. TxHash: {TxHash}",
                        receipt.TransactionHash);

                    return receipt.TransactionHash;
                }

                _logger.LogError(
                    "ConfigurePresale failed (reverted). TxHash: {TxHash}",
                    receipt.TransactionHash);

                return null;
            }
            catch (SmartContractRevertException revertEx)
            {
                _logger.LogError(
                    revertEx,
                    "Contract revert in configurePresale: {Message}",
                    revertEx.Message);

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in configurePresale.");
                return null;
            }
        }

        public async Task<BigInteger> PreSaleOrderGetNonceAsync(string address, string saleId)
        {
            try
            {

                var insuranceContract = _bep20Web3.Eth.GetContract(
                    PreSaleContractAbi,
                   _settings.PreSaleContractAddress
                );

                var noncesFunction = insuranceContract.GetFunction("nonces");
                var saleIdBytes32 = HexToByteArray32(saleId);

                var nonce = await noncesFunction.CallAsync<BigInteger>(
                    address,
                    saleIdBytes32
                );

                _logger.LogInformation(
                    "Nonce for address {address} and saleId {saleId}: {Nonce}",
                    address,
                    saleId,
                    nonce
                );

                return nonce;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error getting nonce for address {UserAddress} and saleId {saleId}",
                    address,
                    saleId
                );

                throw;
            }
        }


        private object[] MapVestingData(List<PreSaleReleaseStep> releaseSchedule)
        {
            if (releaseSchedule == null || !releaseSchedule.Any())
                throw new BadRequestException("Release schedule is required.");

            int totalBps = 0;

            var result = releaseSchedule
                .OrderBy(x => x.ReleaseDate)
                .Select(x =>
                {
                    var dateUtc = NormalizeToUtc(x.ReleaseDate);

                    var timestamp = (ulong)new DateTimeOffset(dateUtc)
                        .ToUnixTimeSeconds();

                    var bps = (ushort)Math.Round(
                        x.Percentage * 100m,
                        MidpointRounding.AwayFromZero);

                    totalBps += bps;

                    return new object[]
                    {
                        timestamp,
                        bps
                    };
                })
                .ToArray();

            if (totalBps != 10000)
                throw new BadRequestException("Total vesting must equal 100% (10000 bps).");

            return result;
        }

        DateTime NormalizeToUtc(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Utc)
                return dt;

            if (dt.Kind == DateTimeKind.Local)
                return dt.ToUniversalTime();

            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }



        public async Task<Dictionary<string, decimal>> GetPreSaleContractBalancesAsync()
        {
            var balances = new Dictionary<string, decimal>();
            var contractAddress = _settings.PreSaleContractAddress;
            try
            {
                foreach (var token in _availableTokenData.Where(q => q.SyncPrice))
                {
                    try
                    {
                        var erc20 = _bep20Web3.Eth.GetContract(ERC20Abi, token.Address);
                        var balanceOf = erc20.GetFunction("balanceOf");

                        var balance = await balanceOf.CallAsync<BigInteger>(contractAddress);
                        balances[token.Name] = UnitConversion.Convert.FromWei(balance, token.PriceDecimalPlaces);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error getting balance for {token.Name}: {ex.Message}");
                        balances[token.Name] = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error in GetContractBalancesAsync: {ex.Message}");

                if (!balances.ContainsKey("BNB"))
                    balances["BNB"] = 0;
            }

            return balances;
        }

        public async Task<decimal> GetPreSaleContractSingleBalanceAsync(string tokenName)
        {

            var network = "BEP20";
            var tokenData = ValidateToken(tokenName, network);
            var contractAddress = _settings.PreSaleContractAddress;

            try
            {
                var erc20 = _bep20Web3.Eth.GetContract(ERC20Abi, tokenData.Address);
                var balanceOf = erc20.GetFunction("balanceOf");

                var balance = await balanceOf.CallAsync<BigInteger>(contractAddress);
                var tokenBalance = UnitConversion.Convert.FromWei(balance, tokenData.PriceDecimalPlaces);
                return tokenBalance;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting balance for {tokenData.Name}: {ex.Message}");
                return 0;
            }
        }



        #endregion


        #region Swap Methods

        public async Task<(decimal Fee, string token)> SwapGetEstimatedFeeAsync(GetSwapEstimatedFeeUpdate update)
        {
            if (update == null)
                throw new BadRequestException("Update is required.");

            if (update.SourceAmoutInWei <= 0)
                throw new BadRequestException("Amount must be greater than zero.");

            if (string.Equals(update.SourceNetwork, update.DestinationNetwork, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "EstimateFee skipped (same network). Network: {Network}",
                    update.SourceNetwork
                );

                return ConvertFeeFromWeiByNetwork(0, update.SourceNetwork);
            }

            //var estimateReturnFee = await EstimateReturnFeeAsync(update.SourceNetwork, update.DestinationNetwork);

            try
            {
                var web3 = GetWeb3(update.SourceNetwork);
                var contractAddress = GetSwapContractAddress(update.SourceNetwork);

                var contract = web3.Eth.GetContract(SwapAbi, contractAddress);
                var function = contract.GetFunction("estimateFee");

                var swapIdBytes = HexToByteArray32(update.SwapReference);

                var param = new object[]
                {
                    swapIdBytes,
                    update.DstEid,
                    update.SourceTokenAddress,
                    update.DestinationTokenAddress,
                    update.SourceAmoutInWei,
                    BigInteger.Zero,
                    update.DestinationWallet,
                    update.Paths.ToArray()
                    //BuildBscPath(update.SourceTokenAddress, update.DestinationTokenAddress)
                };

                var options = BuildLzOptions();

                var result = await function.CallAsync<BigInteger>(
                    param,
                    options,
                    options,
                    update.EstimatedReturnFee
                ); 

                _logger.LogInformation(
                    "EstimateFee | SrcNet: {Src} | DstNet: {Dst} | Amount: {Amount} | Fee: {Fee}",
                    update.SourceNetwork,
                    update.DestinationNetwork,
                    update.SourceAmoutInWei,
                    result
                );

                if (result < 0)
                {
                    throw new BadRequestException("Invalid fee returned from contract.");
                }

                return ConvertFeeFromWeiByNetwork(result, update.SourceNetwork);
            }
            catch (SmartContractRevertException revertEx)
            {
                _logger.LogError(
                    revertEx,
                    "Contract revert in estimateFee: {Message}",
                    revertEx.Message
                );

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SwapGetEstimatedFeeAsync");
                throw;
            }
        }

        private (decimal Fee, string token) ConvertFeeFromWeiByNetwork(BigInteger fee, string network)
        {
            return network?.ToUpper() switch
            {
                "ERC20" => (ConvertFromWei(fee, 18), "ETH"),
                "BEP20" => (ConvertFromWei(fee, 18), "BNB"),
                "TRC20" => (ConvertFromWei(fee, 6), "TRON"),
                _ => throw new BadRequestException($"Invalid network: {network}")
            };
        }

        public async Task<BigInteger> SwapGetOutputAmountAsync(SwapGetOutputAmount update)
        {
            if (update == null)
                throw new BadRequestException("Update is required.");

            if (update.SourceAmountInWei <= 0)
                throw new BadRequestException("SourceAmountInWei must be greater than zero.");

            try
            {
                var contract = _bep20Web3.Eth.GetContract(
                    SwapAbi,
                    _settings.BEP20SwapContractAddress
                );

                var function = contract.GetFunction("getOutputAmount");

                var result = await function.CallAsync<BigInteger>(
                    update.SrcEid,
                    update.DstEid,
                    update.SourceAmountInWei,
                    update.Paths.ToArray()

                    //BuildBscPath(update.SourceTokenAddress, update.DestinationTokenAddress)
                );

                _logger.LogInformation(
                    "GetOutputAmount | TokenIn: {TokenIn} | AmountIn: {AmountIn} | TokenOut: {TokenOut} | Result: {Result}",
                    update.SourceTokenAddress,
                    update.SourceAmountInWei,
                    update.DestinationTokenAddress,
                    result
                );

                return result;
            }
            catch (SmartContractRevertException revertEx)
            {
                _logger.LogError(
                    revertEx,
                    "Contract revert in getOutputAmount: {Message}",
                    revertEx.Message
                );

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SwapGetOutputAmountAsync");
                throw;
            }
        }

        public async Task<BigInteger> EstimateReturnFeeAsync(string sourceNetwork , string destinationNetwork)
        {

            if (sourceNetwork == destinationNetwork)
                return BigInteger.Zero;
            
            try
            {
                var web3 = GetWeb3(destinationNetwork);
                var contractAddress = GetSwapContractAddress(destinationNetwork);

                var contract = web3.Eth.GetContract(
                    SwapAbi,
                    contractAddress);

                var function = contract.GetFunction("estimateReturnFee");

                var srcEid = MapNetworkToEid(sourceNetwork);

                var returnOptions = BuildLzOptions();

                var result = await function.CallAsync<BigInteger>(
                    srcEid,
                    returnOptions);

                _logger.LogInformation(
                    "EstimateReturnFee | SourceNetwork: {SourceNetwork} | DestinationNetwork: {DestinationNetwork} | Fee: {Fee}",
                    sourceNetwork,
                    destinationNetwork,
                    result);

                return result = result + (result * 10 / 100);
            }
            catch (SmartContractRevertException revertEx)
            {
                _logger.LogError(
                    revertEx,
                    "Contract revert in estimateReturnFee: {Message}",
                    revertEx.Message);

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in EstimateReturnFeeAsync");
                throw;
            }
        }

        private byte[] BuildLzOptions()
        {
            //var hex = "0x00030100110100000000000000000000000000030d40";
            var hex = "0x000301001101000000000000000000000000000249f0";

            return Nethereum.Hex.HexConvertors.Extensions.HexByteConvertorExtensions
                .HexToByteArray(hex);
        }
       
        public async Task<BigInteger> GetLiquidityBalanceAsync(string tokenName, string network)
        {
            if (string.IsNullOrWhiteSpace(tokenName))
                throw new BadRequestException("Token name is required.");

            if (string.IsNullOrWhiteSpace(network))
                throw new BadRequestException("Network is required.");

            try
            {
                var token = ValidateToken(tokenName, network);
                var web3 = GetWeb3(network);
                var contractAddress = GetSwapContractAddress(network);

                var contract = web3.Eth.GetContract(SwapAbi, contractAddress);
                var function = contract.GetFunction("getLiquidityBalance");

                var result = await function.CallAsync<BigInteger>(token.Address);

                //_logger.LogInformation(
                //    "GetLiquidityBalance | Network: {Network} | Token: {Token} | Balance: {Balance}",
                //    network,
                //    token.Name,
                //    result);

                return result;
            }
            catch (SmartContractRevertException revertEx)
            {
                _logger.LogError(revertEx, "Contract revert in getLiquidityBalance: {Message}", revertEx.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetLiquidityBalanceAsync");
                throw;
            }
        }

        public uint MapNetworkToEid(string network)
        {
            return network?.ToUpper() switch
            {
                "ERC20" => 30101,
                "BEP20" => 30102,
                "TRC20" => 30420,
                _ => throw new BadRequestException($"Invalid network: {network}")
            };
        }

        private string GetSwapContractAddress(string network)
        {
            return network?.ToUpper() switch
            {
                "ERC20" => _settings.ERC20SwapContractAddress,
                "TRC20" => _settings.TRC20SwapContractAddress,
                "BEP20" => _settings.BEP20SwapContractAddress,
                _ => throw new BadRequestException($"Unsupported network: {network}")
            };
        }

        private Web3 GetWeb3(string network)
        {
            return network?.ToUpper() switch
            {
                "ERC20" => _erc20Web3,
                "TRC20" => _trc20Web3,
                "BEP20" => _bep20Web3,
                _ => throw new BadRequestException($"Unsupported network: {network}")
            };
        }

        private string[] BuildBscPath(string sourceTokenAddress, string destinationTokenAddress)
        {
            if (string.IsNullOrWhiteSpace(sourceTokenAddress) || string.IsNullOrWhiteSpace(destinationTokenAddress))
                return new string[] { sourceTokenAddress ?? destinationTokenAddress };

            var srcAddress = sourceTokenAddress.Trim();
            var dstAddress = destinationTokenAddress.Trim();

            // common BSC addresses
            const string USDT = "0x55d398326f99059fF775485246999027B3197955"; // USDT
            const string USDC = "0x8ac76a51cc950d9822d68b83fe1ad97b32cd580d"; // USDC
            const string WBNB = "0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c"; // WBNB

            var srcToken = _availableTokenData.FirstOrDefault(t => string.Equals(t.Address, srcAddress, StringComparison.OrdinalIgnoreCase)) ??
                throw new NotFoundException();

            var dstToken = _availableTokenData.FirstOrDefault(t => string.Equals(t.Address, dstAddress, StringComparison.OrdinalIgnoreCase)) ??
                throw new NotFoundException();
                
            // if both already BEP20
            var srcIsBep = string.Equals(srcToken.Network, "BEP20", StringComparison.OrdinalIgnoreCase);
            var dstIsBep = string.Equals(dstToken.Network, "BEP20", StringComparison.OrdinalIgnoreCase);

            // utility to find BEP20 counterpart by name
            string FindBepByName(string name)
            {
                if (string.IsNullOrEmpty(name)) return null;
                var match = _availableTokenData.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) && string.Equals(t.Network, "BEP20", StringComparison.OrdinalIgnoreCase));
                return match?.Address;
            }

            // If both on BEP20
            if (srcIsBep && dstIsBep)
            {
                // if either is USDT -> direct
                if (string.Equals(srcToken.Address, USDT, StringComparison.OrdinalIgnoreCase) || string.Equals(dstToken.Address, USDT, StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { srcAddress, dstAddress };
                }

                // otherwise route via WBNB
                return new[] { srcAddress, WBNB, dstAddress };
            }

            // If networks differ (one or both not BEP20)
            // Strategy:
            // 1. Map non-BEP token to its BEP20 counterpart ( USDT on other network -> USDT on BSC)
            // 2. Build path on BSC: prefer [srcBep, USDC, WBNB, dstBep] avoiding duplicates

            // map src to bep20 if needed
            string srcBepAddress = srcAddress;
            if (!srcIsBep)
            {
                // find BEP version by name
                var bep = FindBepByName(srcToken.Name);
                if (!string.IsNullOrEmpty(bep)) srcBepAddress = bep;
            }

            // map dst to bep20 if needed
            string dstBepAddress = dstAddress;
            if (!dstIsBep)
            {
                var bep = FindBepByName(dstToken.Name);
                if (!string.IsNullOrEmpty(bep)) dstBepAddress = bep;
            }

            // ensure we have bep addresses for both, fallback to originals
            var path = new List<string>();
            path.Add(srcBepAddress);

            // Insert USDC if not already present and not equal to src
            if (!string.Equals(srcBepAddress, USDC, StringComparison.OrdinalIgnoreCase) && !string.Equals(dstBepAddress, USDC, StringComparison.OrdinalIgnoreCase))
            {
                path.Add(USDC);
            }

            // then WBNB
            if (!string.Equals(path.Last(), WBNB, StringComparison.OrdinalIgnoreCase) && !string.Equals(dstBepAddress, WBNB, StringComparison.OrdinalIgnoreCase))
            {
                path.Add(WBNB);
            }

            // finally destination
            if (!string.Equals(path.Last(), dstBepAddress, StringComparison.OrdinalIgnoreCase))
                path.Add(dstBepAddress);

            // remove any nulls and duplicates in sequence
            var final = path.Where(a => !string.IsNullOrEmpty(a)).Select(a => a).ToList();

            // normalize duplicates adjacent
            var dedup = new List<string>();
            foreach (var a in final)
            {
                if (dedup.Count == 0 || !string.Equals(dedup.Last(), a, StringComparison.OrdinalIgnoreCase))
                    dedup.Add(a);
            }

            return dedup.ToArray();
        }

        #endregion



        #region Balance Methods


        public async Task<Dictionary<string, decimal>> GetBep20SwapContractBalancesAsync(List<string> symbols = null)
        {
            var balances = new Dictionary<string, decimal>();
            var contractAddress = _settings.BEP20SwapContractAddress;
            try
            {
                if (symbols == null || !symbols.Any())
                {
                    foreach (var token in _availableTokenData.Where(q => q.CanSwap && q.Network == "BEP20"))
                    {
                        try
                        {
                            var erc20 = _bep20Web3.Eth.GetContract(ERC20Abi, token.Address);
                            var balanceOf = erc20.GetFunction("balanceOf");

                            var balance = await balanceOf.CallAsync<BigInteger>(contractAddress);
                            balances[token.Name] = UnitConversion.Convert.FromWei(balance, token.PriceDecimalPlaces);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error getting balance for {token.Name}: {ex.Message}");
                            balances[token.Name] = 0;
                        }
                    }
                }
                else
                {
                    foreach (var tokenIn in symbols)
                    {
                        var token = ValidateToken(tokenIn, "BEP20");
                        if (!token.CanSwap || token.Network != "BEP20") continue;

                        try
                        {
                            var erc20 = _bep20Web3.Eth.GetContract(ERC20Abi, token.Address);
                            var balanceOf = erc20.GetFunction("balanceOf");

                            var balance = await balanceOf.CallAsync<BigInteger>(contractAddress);
                            balances[token.Name] = UnitConversion.Convert.FromWei(balance, token.PriceDecimalPlaces);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error getting balance for {token.Name}: {ex.Message}");
                            balances[token.Name] = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error in GetContractBalancesAsync: {ex.Message}");

                if (!balances.ContainsKey("BNB"))
                    balances["BNB"] = 0;
            }

            return balances;
        }

        public async Task<Dictionary<string, decimal>> GetERC20SwapContractBalancesAsync(List<string> symbols = null)
        {
            var balances = new Dictionary<string, decimal>();
            var contractAddress = _settings.ERC20SwapContractAddress;
            try
            {
                if (symbols == null || !symbols.Any())
                {
                    foreach (var token in _availableTokenData.Where(q => q.CanSwap && q.Network == "ERC20"))
                    {
                        try
                        {
                            var erc20 = _erc20Web3.Eth.GetContract(ERC20Abi, token.Address);
                            var balanceOf = erc20.GetFunction("balanceOf");

                            var balance = await balanceOf.CallAsync<BigInteger>(contractAddress);
                            balances[token.Name] = UnitConversion.Convert.FromWei(balance, token.PriceDecimalPlaces);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error getting balance for {token.Name}: {ex.Message}");
                            balances[token.Name] = 0;
                        }
                    }
                }
                else
                {
                    foreach (var tokenIn in symbols)
                    {
                        var token = ValidateToken(tokenIn, "ERC20");
                        if (!token.CanSwap || token.Network != "ERC20") continue;

                        try
                        {
                            var erc20 = _erc20Web3.Eth.GetContract(ERC20Abi, token.Address);
                            var balanceOf = erc20.GetFunction("balanceOf");

                            var balance = await balanceOf.CallAsync<BigInteger>(contractAddress);
                            balances[token.Name] = UnitConversion.Convert.FromWei(balance, token.PriceDecimalPlaces);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error getting balance for {token.Name}: {ex.Message}");
                            balances[token.Name] = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error in GetContractBalancesAsync: {ex.Message}");

                if (!balances.ContainsKey("BNB"))
                    balances["BNB"] = 0;
            }

            return balances;
        }

        public async Task<Dictionary<string, decimal>> GetTRC20ContractBalancesTronScanAsync(List<string> symbols = null)
        {
            try
            {
                using var httpClient = new HttpClient();

                var url = $"https://apilist.tronscan.org/api/account?address={_settings.TRC20SwapContractAddress}";
                var response = await httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return new Dictionary<string, decimal>();

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var result = new Dictionary<string, decimal>();

                var tokens = _availableTokenData
                    .Where(t => t.Network == "TRC20")
                    .ToList();

                if (symbols != null && symbols.Any())
                {
                    var symbolSet = symbols.Select(s => s.ToUpper()).ToHashSet();

                    tokens = tokens
                        .Where(t => symbolSet.Contains(t.Name))
                        .ToList();
                }

                var tokenMap = tokens.ToDictionary(t => t.Address.ToLower());

                if (symbols == null || symbols.Contains("TRX", StringComparer.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("balance", out var trxElement))
                    {
                        var trx = trxElement.GetDecimal() / 1_000_000m;
                        result["TRX"] = trx;
                    }
                }


                if (root.TryGetProperty("trc20token_balances", out var trc20Array))
                {
                    foreach (var tokenJson in trc20Array.EnumerateArray())
                    {
                        var contractAddress = tokenJson.GetProperty("tokenId").GetString()?.ToLower();
                        var balanceStr = tokenJson.GetProperty("balance").GetString();

                        if (string.IsNullOrEmpty(contractAddress))
                            continue;

                        if (!tokenMap.TryGetValue(contractAddress, out var token))
                            continue;

                        if (decimal.TryParse(balanceStr, out var balance))
                        {
                            var finalBalance = balance / (decimal)Math.Pow(10, token.AmountDecimalPlaces);

                            result[token.Name] = finalBalance;
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TRON balance fetch error");
                return new Dictionary<string, decimal>();
            }
        }


        public async Task<decimal> GetBEP20WalletAddressSingleTokenBalanceAsync(string walletAddress, string tokenName)
        {

            var token = ValidateToken(tokenName, "BEP20");
            if (token == null)
                throw new ArgumentException($"Token '{tokenName}' not found in available tokens.");

            var erc20Contract = _bep20Web3.Eth.GetContract(ERC20Abi, token.Address);
            var balanceOfFunction = erc20Contract.GetFunction("balanceOf");
            var callData = balanceOfFunction.GetData(walletAddress).HexToByteArray();

            var calls = new List<MulticallCall>
            {
                new MulticallCall
                {
                    Target = token.Address,
                    CallData = callData
                }
            };

            var returnDataList = await _multicallService.ExecuteBscCallsAsync(calls);

            if (returnDataList == null || returnDataList.Count == 0 || returnDataList[0] == null)
                return 0;

            try
            {
                var parameterDecoder = new ParameterDecoder();
                var parameters = parameterDecoder.DecodeDefaultData(
                    returnDataList[0],
                    new Parameter("uint256", "balance"));

                var rawBalance = (BigInteger)parameters[0].Result;

                var balance = UnitConversion.Convert.FromWei(rawBalance, token.PriceDecimalPlaces);

                return balance;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error decoding token balance for {token.Name}: {ex.Message}");
                throw new BaseException("An error happened When getting wallet balance!");
            }
        }

        public async Task<decimal> GetERC20WalletAddressSingleTokenBalanceAsync(string walletAddress, string tokenName)
        {

            var token = ValidateToken(tokenName, "ERC20");
            if (token == null)
                throw new BadRequestException($"Token '{tokenName}' not found in available tokens.");

            var erc20Contract = _erc20Web3.Eth.GetContract(ERC20Abi, token.Address);
            var balanceOfFunction = erc20Contract.GetFunction("balanceOf");
            var callData = balanceOfFunction.GetData(walletAddress).HexToByteArray();

            var calls = new List<MulticallCall>
            {
                new MulticallCall
                {
                    Target = token.Address,
                    CallData = callData
                }
            };

            var returnDataList = await _multicallService.ExecuteEthereumCallsAsync(calls);

            if (returnDataList == null || returnDataList.Count == 0 || returnDataList[0] == null)
                return 0;

            try
            {
                var parameterDecoder = new ParameterDecoder();
                var parameters = parameterDecoder.DecodeDefaultData(
                    returnDataList[0],
                    new Parameter("uint256", "balance"));

                var rawBalance = (BigInteger)parameters[0].Result;

                var balance = UnitConversion.Convert.FromWei(rawBalance, token.PriceDecimalPlaces);

                return balance;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error decoding token balance for {token.Name}: {ex.Message}");
                throw new BaseException("An error happened When getting wallet balance!");
            }
        }

        public async Task<decimal> GetTRC20UsdtBalanceAsync(string walletAddress)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(walletAddress))
                    return 0;

                using var httpClient = new HttpClient();

                var url = $"https://apilist.tronscan.org/api/account?address={walletAddress}";
                var response = await httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return 0;

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("trc20token_balances", out var trc20Array))
                {
                    foreach (var token in trc20Array.EnumerateArray())
                    {
                        var tokenAbbr = token.GetProperty("tokenAbbr").GetString();

                        if (!string.Equals(tokenAbbr, "USDT", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var balanceStr = token.GetProperty("balance").GetString();

                        if (decimal.TryParse(balanceStr, out var balance))
                        {
                            return balance / 1_000_000m;
                        }
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TRON USDT balance fetch error");
                return 0;
            }
        }


        #endregion


        #region Utility Methods (Unchanged)
        public BigInteger ConvertToWei(decimal amount, int decimals = 18)
        {
            if (amount < 0) throw new ArgumentException("Amount must be a positive number.");
            var factor = BigInteger.Pow(10, decimals);
            return (BigInteger)(amount * (decimal)factor);
        }

        public decimal ConvertFromWei(BigInteger weiAmount, int decimals = 18)
        {
            if (weiAmount < 0) throw new ArgumentException("Amount must be a positive integer.");
            var factor = (decimal)BigInteger.Pow(10, decimals);
            return (decimal)weiAmount / factor;
        }

        #endregion


        #region Stake 

        /// <summary>
        /// Preview accrued profit for a deposit
        /// </summary>
        /// <param name="depositId">Deposit Id (bytes32 hex string)</param>
        /// <param name="network">Network type (BEP20 / ERC20)</param>
        /// <returns>Claimable profit amount</returns>
        /// <exception cref="BadRequestException"></exception>
        public async Task<BigInteger> StakeBEP20PreviewAccruedProfitAsync(string depositId)
        {
            if (string.IsNullOrWhiteSpace(depositId))
                throw new BadRequestException("Deposit ID is null or empty.");

            try
            {
                Web3 web3;
                string contractAddress;


                web3 = _bep20Web3;
                contractAddress = _settings.StakeContractAddress;


                var contract = web3.Eth.GetContract(StakeAbi, contractAddress);

                var function = contract.GetFunction("previewAccruedProfit");

                var depositIdBytes = HexToByteArray32(depositId);

                var result = await function.CallAsync<BigInteger>(
                    depositIdBytes
                );

                return result;
            }
            catch (SmartContractRevertException revertEx)
            {
                _logger.LogError(
                    revertEx,
                    "Contract revert error during previewAccruedProfit: {Message}",
                    revertEx.Message);

                throw new BaseException("Blockchain contract reverted.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error during previewAccruedProfit.");

                throw new BaseException("An error happened while previewing accrued profit.");
            }
        }

        #endregion


       

        private AvailableTokenData ValidateToken(string tokenName, string network = null)
        {

            if (tokenName == null)
                throw new BadRequestException($"Unsupported token name! {tokenName}");

            if (network == null)
            {
                var tokenData = _availableTokenData.FirstOrDefault(q => q.Name.Equals(tokenName, StringComparison.OrdinalIgnoreCase))
                 ?? throw new BadRequestException($"Unsupported token name! {tokenName}");
                return tokenData;
            }
            else
            {
                var tokenData = _availableTokenData.FirstOrDefault(q => q.Name.Equals(tokenName, StringComparison.OrdinalIgnoreCase) && q.Network == network)
                 ?? throw new BadRequestException($"Unsupported token name and network! {tokenName}");
                return tokenData;

            }

        }

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

        private async Task<BigInteger> GetOptimalGasPriceAsync()
        {
            try
            {
                var currentGasPrice = await _bep20Web3.Eth.GasPrice.SendRequestAsync();

                var suggestedGasPrice = (BigInteger)((decimal)currentGasPrice.Value * 1.2m);

                var minGasPrice = UnitConversion.Convert.ToWei(_settings.GetMinGasPriceGwei(), UnitConversion.EthUnit.Gwei);
                var maxGasPrice = UnitConversion.Convert.ToWei(_settings.GetMaxGasPriceGwei(), UnitConversion.EthUnit.Gwei);

                var optimalPrice = BigInteger.Min(BigInteger.Max(suggestedGasPrice, minGasPrice), maxGasPrice);

                _logger.LogInformation($"Using gas price: {UnitConversion.Convert.FromWei(optimalPrice, UnitConversion.EthUnit.Gwei)} Gwei");
                return optimalPrice;
            }
            catch
            {
                var defaultPrice = UnitConversion.Convert.ToWei(_settings.GetDefaultGasPriceGwei(), UnitConversion.EthUnit.Gwei);
                _logger.LogWarning($"Using DEFAULT gas price: {_settings.GetDefaultGasPriceGwei()} Gwei");
                return defaultPrice;
            }
        }

    }
}




//public async Task<Dictionary<string, decimal>> GetBalancesMultiCallAsync(ContractType contractType)
//{
//    var balances = new Dictionary<string, decimal>();
//    var contractAddress = GetContractAddressByType(contractType);

//    var tokens = _availableTokenData;

//    if (tokens == null || !tokens.Any())
//        return balances;

//    var calls = new List<MulticallCall>();
//    var tokenList = tokens.ToList();

//    foreach (var token in tokenList)
//    {
//        var erc20Contract = _bep20Web3.Eth.GetContract(ERC20Abi, token.Address);
//        var balanceOfFunction = erc20Contract.GetFunction("balanceOf");
//        calls.Add(new MulticallCall
//        {
//            Target = token.Address,
//            CallData = balanceOfFunction.GetData(contractAddress).HexToByteArray()
//        });
//    }

//    var returnDataList = await _multicallService.ExecuteCallsAsync(calls);

//    var parameterDecoder = new ParameterDecoder();

//    for (int i = 0; i < tokenList.Count; i++)
//    {
//        try
//        {
//            if (i < returnDataList.Count && returnDataList[i] != null && returnDataList[i].Length > 0)
//            {
//                var parameters = parameterDecoder.DecodeDefaultData(
//                    returnDataList[i],
//                    new Parameter("uint256", "balance"));

//                var rawBalance = (BigInteger)parameters[0].Result;

//                balances[tokenList[i].Name] = UnitConversion.Convert.FromWei(
//                    rawBalance, tokenList[i].PriceDecimalPlaces);
//            }
//            else
//            {
//                balances[tokenList[i].Name] = 0;
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error processing token {tokenList[i].Name}: {ex.Message}");
//            balances[tokenList[i].Name] = 0;
//        }
//    }

//    return balances;
//}//public async Task<Dictionary<string, decimal>> GetTRC20ContractBalancesTronGridAsync(List<string> symbols = null)
//{
//    try
//    {
//        using var httpClient = new HttpClient();

//        var url = $"https://api.trongrid.io/v1/accounts/{_settings.TRC20SwapContractAddress}";
//        //var url = $"https://apilist.tronscan.org/api/account?address={_settings.TRC20SwapContractAddress}";
//        var response = await httpClient.GetAsync(url);

//        if (!response.IsSuccessStatusCode)
//            return new Dictionary<string, decimal>();

//        var json = await response.Content.ReadAsStringAsync();

//        using var doc = JsonDocument.Parse(json);
//        var root = doc.RootElement;

//        var result = new Dictionary<string, decimal>();


//        var tokensToCheck = _availableTokenData
//            .Where(q =>q.CanSwap)
//            .Where(t => t.Network == "TRC20")
//            .ToList();

//        if (symbols != null && symbols.Any())
//        {
//            var symbolSet = symbols.Select(s => s.ToUpper()).ToHashSet();
//            tokensToCheck = tokensToCheck
//                .Where(t => symbolSet.Contains(t.Name))
//                .ToList();
//        }

//        // TRX (native)
//        if (symbols == null || symbols.Contains("TRX", StringComparer.OrdinalIgnoreCase))
//        {
//            if (root.TryGetProperty("balance", out var trxElement))
//            {
//                var trx = trxElement.GetDecimal() / 1_000_000m;
//                result["TRX"] = trx;
//            }
//        }

//        // TRC20 tokens
//        if (root.TryGetProperty("trc20", out var trc20Array))
//        {
//            foreach (var tokenObj in trc20Array.EnumerateArray())
//            {
//                foreach (var property in tokenObj.EnumerateObject())
//                {
//                    var contractAddress = property.Name;
//                    var balanceStr = property.Value.GetString();

//                    var token = tokensToCheck.FirstOrDefault(t =>
//                        string.Equals(t.Address, contractAddress, StringComparison.OrdinalIgnoreCase));

//                    if (token == null)
//                        continue;

//                    if (decimal.TryParse(balanceStr, out var balance))
//                    {
//                        var finalBalance = balance / (decimal)Math.Pow(10, token.AmountDecimalPlaces);

//                        result[token.Name] = finalBalance;
//                    }
//                }
//            }
//        }

//        return result;
//    }
//    catch (Exception ex)
//    {
//        _logger.LogError(ex, "TRON balance fetch error");
//        return new Dictionary<string, decimal>();
//    }
//}


//public async Task<Dictionary<string, decimal>> GetWalletBEP20AddressBalanceAsync(string walletAddress)
//{
//    var balances = new Dictionary<string, decimal>();
//    var tokens = _availableTokenData;

//    if (tokens == null || !tokens.Any())
//        return balances;

//    var calls = new List<MulticallCall>();
//    var tokenList = tokens.ToList();

//    foreach (var token in tokenList)
//    {
//        var erc20Contract = _bep20Web3.Eth.GetContract(ERC20Abi, token.Address);
//        var balanceOfFunction = erc20Contract.GetFunction("balanceOf");
//        calls.Add(new MulticallCall
//        {
//            Target = token.Address,
//            CallData = balanceOfFunction.GetData(walletAddress).HexToByteArray()
//        });
//    }

//    var returnDataList = await _multicallService.ExecuteCallsAsync(calls);

//    var parameterDecoder = new ParameterDecoder();

//    for (int i = 0; i < tokenList.Count; i++)
//    {
//        try
//        {
//            if (i < returnDataList.Count && returnDataList[i] != null && returnDataList[i].Length > 0)
//            {
//                var parameters = parameterDecoder.DecodeDefaultData(
//                    returnDataList[i],
//                    new Parameter("uint256", "balance"));

//                var rawBalance = (BigInteger)parameters[0].Result;

//                balances[tokenList[i].Name.ToUpper()] = UnitConversion.Convert.FromWei(
//                    rawBalance, tokenList[i].PriceDecimalPlaces);
//            }
//            else
//            {
//                balances[tokenList[i].Name.ToUpper()] = 0;
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error processing token balance {tokenList[i].Name}: {ex.Message}");
//            throw new BaseException("An error happened When getting wallet balance!");
//            //balances[tokenList[i].Name] = 0;
//        }
//    }

//    return balances;
//}