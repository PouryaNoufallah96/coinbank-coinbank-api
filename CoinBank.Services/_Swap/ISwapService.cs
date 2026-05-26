using CoinBank.Services._Swap.DTOs.Results;
using CoinBank.Services._Swap.DTOs.Updates;

namespace CoinBank.Services._Swap
{
    public interface ISwapService
    {
        Task<SwapCreatedResult> CreateSwapAsync(CreateSwapUpdate update, string walletAddress,string walletType, string publicKey);
        Task<SwapListResult> GetSwapHistoryAsync(SwapHistoryUpdate update,string walletAddress, string publicKey);
        Task<SwapResult> GetOneSwapByReferenceAsync(SwapReferenceUpdate update, string walletAddress ,string publicKey);
        Task RemoveNotRegisteredSwapsAsync();

        Task AddTransactionToSwapAsync(AddTransactionToSwapUpdate update);

        Task InitializeSwapStorageAsync();
        Task UpdateSingleTokenInStorageAsync(string tokenAddress, string network);

    }
}
