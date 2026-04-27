using CoinBank.Domain.Collections;
using CoinBank.Services._PreSaleOrder.DTOs.Results;
using CoinBank.Services._PreSaleOrder.DTOs.Updates;

namespace CoinBank.Services._PreSaleOrder
{
    public interface IPreSaleOrderService
    {
        Task<PreSaleOrderResult> CreatePreSaleOrderAsync(CreatePreSaleOrderUpdate update, string publicKey, string evmWalletAddress);
        Task<PreSaleOrderListResult> GetPreSaleOrderHistoryAsync(GetPreSaleOrderHistoryUpdate update, string publicKey, string evmWalletAddress);

        Task ActivatePreSaleOrderForInternalUsageAsync(string presaleOrderId , string registerHash);
        //Task MakeCompeletePreSaleOrderStateByPreSaleReferenceAsync(string preSaleReference);

        Task<PreSaleOrder> GetOneByReferenceForInternalUsageAsync(string preSaleOrderRef);

        Task<List<PreSaleOrderWalletStatsResult>> GetWalletStatsAsync(GetPreSaleOrderWalletStatsUpdate update, string publicKey, string evmWalletAddress);
        Task<PreSaleOrderDetailResult> GetOnePreSaleOrderDetailAsync(GetOnePreSaleOrderDetailUpdate update, string publicKey, string evmWalletAddress);

        //internal
        Task RemoveNotRegisteredPreSaleOrderAsync();
        Task ProcessReleaseOrderAsync();
        //Task UpdatePreSaleReleaseStepForAddTransactionAsync(string preSaleOrderRef, string txHash, string claimedAmount);
    }
}
