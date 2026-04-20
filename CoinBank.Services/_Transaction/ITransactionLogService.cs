using CoinBank.Services._Transaction.DTOs.Updates;
using System.Numerics;

namespace CoinBank.Services._Transaction
{
    public interface ITransactionLogService
    {
        Task CreatePreSaleOrderCreateLogAsync(PreSaleOrderCreateLog input);
        Task CreatePreSaleReleaseClaimedLogAsync(PreSaleReleaseClaimedLog input);
        Task<BigInteger> GetLastCheckedBlockNumberAsync();
    }
}
