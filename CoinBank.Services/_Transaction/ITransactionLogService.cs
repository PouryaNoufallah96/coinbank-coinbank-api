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
        Task CreateSwapCompletedLogAsync(SwapCompletedLog input);
        Task CreateSwapRefundedLogAsync(SwapRefundedLog input);
        Task<BigInteger> GetSwapLastCheckedBlockNumberAsync(string network);

        //Stake 
        Task CreateDepositCreatedLogAsync(DepositCreatedLog input);
        Task CreateEarlyWithdrawnLogAsync(EarlyWithdrawnLog input);
        Task CreateProfitWithdrawnLogAsync(ProfitWithdrawnLog input);
        Task CreateWithdrawnLogAsync(WithdrawnLog input);
        Task<BigInteger> GetDepositLastCheckedBlockNumberAsync(string network = "BEP20");





        Task<BigInteger> GetLastCheckedBlockNumberAsync();
    }
}
