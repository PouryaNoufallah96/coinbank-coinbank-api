using CoinBank.Services._MultiCallService.DTOs;

namespace CoinBank.Services._MultiCallService;

public interface IMultiCallService
{
    Task<List<byte[]>> ExecuteBscCallsAsync(List<MulticallCall> calls);
    Task<List<MulticallResult>> ExecuteBscTryCallsAsync(List<MulticallCall> calls, bool requireSuccess = false);
    Task<List<byte[]>> ExecuteEthereumCallsAsync(List<MulticallCall> calls);
    Task<List<MulticallResult>> ExecuteEthereumTryCallsAsync(List<MulticallCall> calls, bool requireSuccess = false);


}
