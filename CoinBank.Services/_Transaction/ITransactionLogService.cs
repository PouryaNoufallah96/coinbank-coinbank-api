using CoinBank.Services._Transaction.DTOs.Updates;
using System.Numerics;

namespace CoinBank.Services._Transaction
{
    public interface ITransactionLogService
    {

        //presale
        Task CreatePreSaleOrderCreateLogAsync(PreSaleOrderCreateLog input);
        Task CreatePreSaleReleaseClaimedLogAsync(PreSaleReleaseClaimedLog input);
        Task<BigInteger> GetPreSaleOrderLastCheckedBlockNumberAsync();


        //swap
        Task CreateSwapInitiatedLogAsync(SwapInitiatedLog input);
        Task CreateSwapExecutedLogAsync(SwapExecutedLog input); 
        Task CreateSwapFailedLogAsync(SwapFailedLog input);
        Task<BigInteger> GetSwapLastCheckedBlockNumberAsync(); 


        Task<BigInteger> GetLastCheckedBlockNumberAsync();
    }
}
