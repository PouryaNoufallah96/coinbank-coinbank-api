using CoinBank.Services._Swap.DTOs.Results;
using CoinBank.Services._Swap.DTOs.Updates;

namespace CoinBank.Services._Swap
{
    public interface ISwapService
    {
        Task<SwapCreatedResult> CreateSwapAsync(CreateSwapUpdate update, string EVMwalletAddress, string TronWalletAddress, string publicKey);
        Task<SwapListResult> GetSwapHistoryAsync(SwapHistoryUpdate update,string EVMwalletAddress, string TronWalletAddress, string publicKey);
        Task<SwapResult> GetOneSwapByReferenceAsync(SwapReferenceUpdate update, string EVMwalletAddress, string TronWalletAddress, string publicKey);
        Task RemoveNotRegisteredSwapsAsync();

        Task AddTransactionToSwapAsync(AddTransactionToSwapUpdate update);

        Task InitializeSwapStorageAsync();
        Task UpdateSingleTokenInStorageAsync(string tokenAddress, string network);

    }
}
