using CoinBank.Services._Stake.DTOs.Results;
using CoinBank.Services._Stake.DTOs.Updates;

namespace CoinBank.Services._Stake
{
    public interface IStakeService
    {
        Task<StakeResult> CreateStakeAsync(CreateStakeUpdate update, string publicKey, string evmWalletAddress);
        Task<StakeListResult> GetStakeHistoryAsync(StakeHistoryUpdate update, string publicKey, string evmWalletAddress);
        Task<StakeDetailResult> GetStakeDetailAsync(StakeDetailUpdate update, string publicKey, string evmWalletAddress);
        Task<List<StakeWalletStatsResult>> GetWalletStatsAsync(GetStakeWalletStatsUpdate update, string publicKey, string evmWalletAddress);
    }
}
 