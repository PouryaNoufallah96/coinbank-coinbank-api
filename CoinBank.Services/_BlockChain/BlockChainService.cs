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
using Utilities.Exceptions.Common;
using static Utilities.Constants.RegisterMode;


namespace CoinBank.Services._BlockChain
{
    public class BlockChainService : IBlockChainService, ISingletonDependency
    {
        private const string PreSaleContractAbi = TokenForwardSaleAbi.PreSaleAbi;
        private const string ERC20Abi = TokenForwardSaleAbi.ERC20Abi;
        private readonly BlockChainSettings _settings;
        private readonly ILogger<BlockChainService> _logger;
        private readonly IMultiCallService _multicallService;
        private readonly AvailableTokensSettings _availableTokenData;
        private readonly Web3 _web3;
        private readonly Account _account;
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

            _account = new Account(_settings.PrivateKey, _settings.ChainId);
            _web3 = new Web3(_account, _settings.RpcUrl2);

            _web3.TransactionManager.UseLegacyAsDefault = true;
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
                var contract = _web3.Eth.GetContract(PreSaleContractAbi, _settings.PreSaleContractAddress);
                var function = contract.GetFunction("claimTokensByOperator");

                var presaleIdBytes = HexToByteArray32(presaleId);
                var orderIdBytes = HexToByteArray32(orderId);

                var gasPrice = await GetOptimalGasPriceAsync();
                var gas = new Nethereum.Hex.HexTypes.HexBigInteger(
                    _settings.GetDefaultGasLimit());

                var receipt = await function.SendTransactionAndWaitForReceiptAsync(
                    from: _account.Address,
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
                var contract = _web3.Eth.GetContract(PreSaleContractAbi, _settings.PreSaleContractAddress);
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
                    from: _account.Address,
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

                var insuranceContract = _web3.Eth.GetContract(
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

        #endregion













        #region Balance Methods
        public async Task<Dictionary<string, decimal>> GetContractBalancesAsync(ContractType type)
        {
            var balances = new Dictionary<string, decimal>();
            var contractAddress = GetContractAddressByType(type);
            try
            {
                foreach (var token in _availableTokenData)
                {
                    try
                    {
                        var erc20 = _web3.Eth.GetContract(ERC20Abi, token.Address);
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

        public async Task<decimal> GetContractSingleBalanceAsync(string tokenName, ContractType contractType)
        {
            var tokenData = ValidateToken(tokenName);
            var contractAddress = GetContractAddressByType(contractType);

            try
            {
                var erc20 = _web3.Eth.GetContract(ERC20Abi, tokenData.Address);
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

        public async Task<decimal> GetContractRZUSDBalanceAsync(ContractType contractType)
        {
            var contractAddress = GetContractAddressByType(contractType);

            try
            {
                var erc20 = _web3.Eth.GetContract(ERC20Abi, "0xC4A1cc5cA8955a4650BDC109bddf110E33a1e344");
                var balanceOf = erc20.GetFunction("balanceOf");

                var balance = await balanceOf.CallAsync<BigInteger>(contractAddress);
                var tokenBalance = UnitConversion.Convert.FromWei(balance, 18);
                return tokenBalance;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting balance for RZUSD: {ex.Message}");
                return 0;
            }
        }

        private string GetContractAddressByType(ContractType contractType)
        {
            switch (contractType)
            {
                case ContractType.PreSale:
                    return _settings.PreSaleContractAddress;
                case ContractType.Swap:
                    return _settings.SwapContractAddress;
                case ContractType.Stake:
                    return _settings.StakeContractAddress;
                default:
                    throw new BadRequestException("wrong contract type!");
            }
        }

        public async Task<Dictionary<string, decimal>> GetBalancesMultiCallAsync(ContractType contractType)
        {
            var balances = new Dictionary<string, decimal>();
            var contractAddress =  GetContractAddressByType(contractType);

            var tokens = _availableTokenData;

            if (tokens == null || !tokens.Any())
                return balances;

            var calls = new List<MulticallCall>();
            var tokenList = tokens.ToList();

            foreach (var token in tokenList)
            {
                var erc20Contract = _web3.Eth.GetContract(ERC20Abi, token.Address);
                var balanceOfFunction = erc20Contract.GetFunction("balanceOf");
                calls.Add(new MulticallCall
                {
                    Target = token.Address,
                    CallData = balanceOfFunction.GetData(contractAddress).HexToByteArray()
                });
            }

            var returnDataList = await _multicallService.ExecuteCallsAsync(calls);

            var parameterDecoder = new ParameterDecoder();

            for (int i = 0; i < tokenList.Count; i++)
            {
                try
                {
                    if (i < returnDataList.Count && returnDataList[i] != null && returnDataList[i].Length > 0)
                    {
                        var parameters = parameterDecoder.DecodeDefaultData(
                            returnDataList[i],
                            new Parameter("uint256", "balance"));

                        var rawBalance = (BigInteger)parameters[0].Result;

                        balances[tokenList[i].Name] = UnitConversion.Convert.FromWei(
                            rawBalance, tokenList[i].PriceDecimalPlaces);
                    }
                    else
                    {
                        balances[tokenList[i].Name] = 0;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing token {tokenList[i].Name}: {ex.Message}");
                    balances[tokenList[i].Name] = 0;
                }
            }

            return balances;
        }

        public async Task<Dictionary<string, decimal>> GetWalletAddressBalanceAsync(string walletAddress)
        {
            var balances = new Dictionary<string, decimal>();
            var tokens = _availableTokenData;

            if (tokens == null || !tokens.Any())
                return balances;

            var calls = new List<MulticallCall>();
            var tokenList = tokens.ToList();

            foreach (var token in tokenList)
            {
                var erc20Contract = _web3.Eth.GetContract(ERC20Abi, token.Address);
                var balanceOfFunction = erc20Contract.GetFunction("balanceOf");
                calls.Add(new MulticallCall
                {
                    Target = token.Address,
                    CallData = balanceOfFunction.GetData(walletAddress).HexToByteArray()
                });
            }

            var returnDataList = await _multicallService.ExecuteCallsAsync(calls);

            var parameterDecoder = new ParameterDecoder();

            for (int i = 0; i < tokenList.Count; i++)
            {
                try
                {
                    if (i < returnDataList.Count && returnDataList[i] != null && returnDataList[i].Length > 0)
                    {
                        var parameters = parameterDecoder.DecodeDefaultData(
                            returnDataList[i],
                            new Parameter("uint256", "balance"));

                        var rawBalance = (BigInteger)parameters[0].Result;

                        balances[tokenList[i].Name.ToUpper()] = UnitConversion.Convert.FromWei(
                            rawBalance, tokenList[i].PriceDecimalPlaces);
                    }
                    else
                    {
                        balances[tokenList[i].Name.ToUpper()] = 0;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing token balance {tokenList[i].Name}: {ex.Message}");
                    throw new BaseException("An error happened When getting wallet balance!");
                    //balances[tokenList[i].Name] = 0;
                }
            }

            return balances;
        }

        public async Task<decimal> GetWalletAddressSingleTokenBalanceAsync(string walletAddress, string tokenName)
        {
            var token = ValidateToken(tokenName);
            if (token == null)
                throw new ArgumentException($"Token '{tokenName}' not found in available tokens.");

            var erc20Contract = _web3.Eth.GetContract(ERC20Abi, token.Address);
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

            var returnDataList = await _multicallService.ExecuteCallsAsync(calls);

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

        public async Task<Dictionary<string, Dictionary<string, decimal>>> GetWalletsBalancesAsync(
         List<string> walletAddresses)
        {
            var result = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase);

            var tokens = _availableTokenData;
            if (tokens == null || !tokens.Any())
            {
                foreach (var w in walletAddresses)
                    result[w] = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                return result;
            }

            var tokenList = tokens.ToList();
            var calls = new List<MulticallCall>();
            var callMap = new List<(string Wallet, AvailableTokenData Token)>();

            foreach (var wallet in walletAddresses)
            {
                if (!result.ContainsKey(wallet))
                    result[wallet] = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                foreach (var token in tokenList)
                {
                    var erc20Contract = _web3.Eth.GetContract(ERC20Abi, token.Address);
                    var balanceOfFunction = erc20Contract.GetFunction("balanceOf");

                    calls.Add(new MulticallCall
                    {
                        Target = token.Address,
                        CallData = balanceOfFunction.GetData(wallet).HexToByteArray()
                    });

                    callMap.Add((wallet, token));
                }
            }

            if (calls.Count == 0)
                return result;

            List<MulticallResult> rawResults;
            try
            {
                rawResults = await _multicallService.ExecuteCallsTryAsync(calls, requireSuccess: false);
            }
            catch
            {
                var raw = await _multicallService.ExecuteCallsAsync(calls);
                rawResults = raw.Select(r => new MulticallResult { Success = r != null && r.Length > 0, ReturnData = r }).ToList();
            }

            var parameterDecoder = new ParameterDecoder();
            var uintParam = new Parameter("uint256", "balance");

            for (int i = 0; i < callMap.Count; i++)
            {
                var map = callMap[i];
                try
                {
                    var wallet = map.Wallet;
                    var token = map.Token;
                    var tokenNameKey = token.Name?.ToUpper() ?? token.Address;

                    if (i < rawResults.Count && rawResults[i] != null && rawResults[i].ReturnData != null && rawResults[i].ReturnData.Length > 0)
                    {
                        var bytes = rawResults[i].ReturnData;
                        var parameters = parameterDecoder.DecodeDefaultData(bytes, uintParam);
                        var rawBalance = (BigInteger)parameters[0].Result;

                        var decimalBalance = UnitConversion.Convert.FromWei(rawBalance, token.PriceDecimalPlaces);
                        result[wallet][tokenNameKey] = decimalBalance;
                    }
                    else
                    {
                        result[wallet][tokenNameKey] = 0m;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing wallet {callMap[i].Wallet} token {callMap[i].Token?.Name}: {ex.Message}");
                    throw new BaseException("An error happened When getting wallets balances!");

                    var fallbackTokenName = callMap[i].Token?.Name?.ToUpper() ?? callMap[i].Token?.Address;
                    if (!result[callMap[i].Wallet].ContainsKey(fallbackTokenName))
                        result[callMap[i].Wallet][fallbackTokenName] = 0m;
                }
            }

            return result;
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



       

        private AvailableTokenData ValidateToken(string tokenName)
        {

            if (tokenName == null)
                throw new BadRequestException($"Unsupported token name! {tokenName}");

            var tokenData = _availableTokenData.FirstOrDefault(q => q.Name.Equals(tokenName, StringComparison.OrdinalIgnoreCase))
                ?? throw new BadRequestException($"Unsupported token name! {tokenName}");
            return tokenData;
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
                var currentGasPrice = await _web3.Eth.GasPrice.SendRequestAsync();

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
