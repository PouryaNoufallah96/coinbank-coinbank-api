using CoinBank.Services._BlockChain._MultiCallService.DTOs;

namespace CoinBank.Services._BlockChain._MultiCallService;

public interface IMultiCallService
{
    Task<List<byte[]>> ExecuteCallsAsync(List<MulticallCall> calls);
    Task<List<MulticallResult>> ExecuteCallsTryAsync(List<MulticallCall> calls, bool requireSuccess = false);
}
