using CoinBank.Services._PreSaleOrder.DTOs.Results;
using CoinBank.Services._PreSaleOrder.DTOs.Updates;

namespace CoinBank.Services._PreSaleOrder
{
    public interface IPreSaleOrderService
    {
        Task<PreSaleOrderResult> CreatePreSaleOrderAsync(CreatePreSaleOrderUpdate update, string publicKey, string evmWalletAddress);
        Task<PreSaleOrderListResult> GetPreSaleOrderHistoryAsync(GetPreSaleOrderHistoryUpdate update, string publicKey, string evmWalletAddress);

        //Task ActivatePreSaleOrderForInternalUsageAsync(); 
        //Task MakeCompeletePreSaleOrderStateByPreSaleReferenceAsync(string preSaleReference);


        Task<List<PreSaleOrderWalletStatsResult>> GetWalletStatsAsync(GetPreSaleOrderWalletStatsUpdate update, string publicKey, string evmWalletAddress);
        Task<PreSaleOrderDetailResult> GetOnePreSaleOrderDetailAsync(GetOnePreSaleOrderDetailUpdate update, string publicKey, string evmWalletAddress);

        //internal
        Task RemoveNotRegisteredPreSaleOrderAsync();
        Task ProcessReleaseOrderAsync();
    }
}
