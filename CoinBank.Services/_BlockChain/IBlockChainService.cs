using CoinBank.Domain.Collections;
using CoinBank.Services._BlockChain.DTOs.Updates;
using System.Numerics;

namespace CoinBank.Services._BlockChain
{
    public interface IBlockChainService
    {

        //PreSale
        Task<string> PreSaleConfigureAsync(PreSale preSale);
        Task<string> PreSaleOrderClaimTokensByOperatorAsync(string presaleId, string orderId);


        //Swap
        Task<(decimal Fee, string token)> SwapGetEstimatedFeeAsync(GetSwapEstimatedFeeUpdate update);
        Task<BigInteger> SwapGetOutputAmountAsync(SwapGetOutputAmount update);
        uint MapNetworkToEid(string network);


        //balance Methods
        Task<Dictionary<string, decimal>> GetContractBalancesAsync(ContractType contractType);
        Task<decimal> GetContractSingleBalanceAsync(string tokenName, ContractType contractType);
        Task<Dictionary<string, decimal>> GetBalancesMultiCallAsync(ContractType contractType);
        Task<Dictionary<string, decimal>> GetWalletAddressBalanceAsync(string walletAddress);
        Task<decimal> GetWalletAddressSingleTokenBalanceAsync(string walletAddress,string tokenName);
        Task<Dictionary<string, Dictionary<string, decimal>>> GetWalletsBalancesAsync(
           List<string> walletAddresses);
       
        // Utility Methods
        decimal ConvertFromWei(BigInteger weiAmount, int decimals = 18);
        BigInteger ConvertToWei(decimal amount, int decimals = 18);


       
        Task<BigInteger> PreSaleOrderGetNonceAsync(string address, string saleId);

    }
}
