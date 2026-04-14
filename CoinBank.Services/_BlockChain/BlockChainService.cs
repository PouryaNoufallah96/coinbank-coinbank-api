using CoinBank.Services._BlockChain.DTOs.Settings;
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
        private const string ContractAbi = TokenForwardSaleAbi.Value;
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

        #region Balance Methods
        public async Task<Dictionary<string, decimal>> GetContractBalancesAsync()
        {
            var balances = new Dictionary<string, decimal>();

            try
            {
                foreach (var token in _availableTokenData)
                {
                    try
                    {
                        var erc20 = _web3.Eth.GetContract(ERC20Abi, token.Address);
                        var balanceOf = erc20.GetFunction("balanceOf");

                        var balance = await balanceOf.CallAsync<BigInteger>(_settings.ContractAddress);
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

        public async Task<decimal> GetContractSingleBalanceAsync(string tokenName)
        {
            var tokenData = ValidateToken(tokenName);
            try
            {
                var erc20 = _web3.Eth.GetContract(ERC20Abi, tokenData.Address);
                var balanceOf = erc20.GetFunction("balanceOf");

                var balance = await balanceOf.CallAsync<BigInteger>(_settings.ContractAddress);
                var tokenBalance = UnitConversion.Convert.FromWei(balance, tokenData.PriceDecimalPlaces);
                return tokenBalance;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting balance for {tokenData.Name}: {ex.Message}");
                return 0;
            }
        }
        public async Task<decimal> GetContractRZUSDBalanceAsync()
        {
            try
            {
                var erc20 = _web3.Eth.GetContract(ERC20Abi, "0xC4A1cc5cA8955a4650BDC109bddf110E33a1e344");
                var balanceOf = erc20.GetFunction("balanceOf");

                var balance = await balanceOf.CallAsync<BigInteger>(_settings.ContractAddress);
                var tokenBalance = UnitConversion.Convert.FromWei(balance, 18);
                return tokenBalance;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting balance for RZUSD: {ex.Message}");
                return 0;
            }
        }

        public async Task<Dictionary<string, decimal>> GetBalancesMultiCallAsync()
        {
            var balances = new Dictionary<string, decimal>();
            var contractAddress = _settings.ContractAddress;
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


      

        public async Task<BigInteger> GetNonceAsync(string address)
        {
            try
            {
               
                var insuranceContract = _web3.Eth.GetContract(ContractAbi, _settings.ContractAddress);
                var noncesFunction = insuranceContract.GetFunction("nonces");
                var nonce = await noncesFunction.CallAsync<BigInteger>(address);

                _logger.LogInformation("Nonce for {address}: {Nonce}", address, nonce);

                return nonce;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting nonce for address {UserAddress}", address);
                throw;
            }
        }


        private AvailableTokenData ValidateToken(string tokenName)
        {

            if (tokenName == null)
                throw new BadRequestException($"Unsupported token name! {tokenName}");

            var tokenData = _availableTokenData.FirstOrDefault(q => q.Name.Equals(tokenName, StringComparison.OrdinalIgnoreCase))
                ?? throw new BadRequestException($"Unsupported token name! {tokenName}");
            return tokenData;
        }


    }
}
