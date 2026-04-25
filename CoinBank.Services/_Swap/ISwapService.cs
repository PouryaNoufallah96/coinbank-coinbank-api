using CoinBank.Services._Swap.DTOs.Results;
using CoinBank.Services._Swap.DTOs.Updates;

namespace CoinBank.Services._Swap
{
    public interface ISwapService
    {
        Task<SwapResult> CreateSwapAsync(CreateSwapUpdate update, string walletAddress, string publicKey);
    }
}
