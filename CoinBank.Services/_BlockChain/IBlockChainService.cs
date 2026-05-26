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
        Task<BigInteger> GetLiquidityBalanceAsync(string tokenName, string network);
        uint MapNetworkToEid(string network);


        //balance Methods
        Task<Dictionary<string, decimal>> GetPreSaleContractBalancesAsync();
        Task<decimal> GetPreSaleContractSingleBalanceAsync(string tokenName);

        Task<decimal> GetBEP20WalletAddressSingleTokenBalanceAsync(string walletAddress,string tokenName);
        Task<decimal> GetERC20WalletAddressSingleTokenBalanceAsync(string walletAddress,string tokenName);
        Task<Dictionary<string, decimal>> GetBep20SwapContractBalancesAsync(List<string> symbols = null);
        Task<Dictionary<string, decimal>> GetERC20SwapContractBalancesAsync(List<string> symbols = null);
        Task<Dictionary<string, decimal>> GetTRC20ContractBalancesTronScanAsync(List<string> symbols = null);
        Task<decimal> GetTRC20UsdtBalanceAsync(string walletAddress);


        //stake methods
        Task<BigInteger> StakeBEP20PreviewAccruedProfitAsync(string depositId);
       


        // Utility Methods
        decimal ConvertFromWei(BigInteger weiAmount, int decimals = 18);
        BigInteger ConvertToWei(decimal amount, int decimals = 18);

       
        Task<BigInteger> PreSaleOrderGetNonceAsync(string address, string saleId);
    }
}

//Task<Dictionary<string, decimal>> GetBalancesMultiCallAsync(ContractType contractType);
//Task<Dictionary<string, decimal>> GetWalletBEP20AddressBalanceAsync(string walletAddress);