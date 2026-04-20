using CoinBank.Domain.Collections;
using System.Numerics;

namespace CoinBank.Services._BlockChain
{
    public interface IBlockChainService
    {

        //PreSale
        Task<string> ConfigurePresaleAsync(PreSale preSale);


        //balance Methods
        Task<Dictionary<string, decimal>> GetContractBalancesAsync();
        Task<decimal> GetContractSingleBalanceAsync(string tokenName);
        Task<Dictionary<string, decimal>> GetBalancesMultiCallAsync();
        Task<Dictionary<string, decimal>> GetWalletAddressBalanceAsync(string walletAddress);
        Task<decimal> GetWalletAddressSingleTokenBalanceAsync(string walletAddress,string tokenName);
        Task<Dictionary<string, Dictionary<string, decimal>>> GetWalletsBalancesAsync(
           List<string> walletAddresses);
       
        // Utility Methods
        decimal ConvertFromWei(BigInteger weiAmount, int decimals = 18);
        BigInteger ConvertToWei(decimal amount, int decimals = 18);


       
        Task<BigInteger> GetNonceAsync(string address, string saleId);

    }
}
