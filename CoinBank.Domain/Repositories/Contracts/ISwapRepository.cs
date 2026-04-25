using CoinBank.Domain.Collections;
using Utilities.MongoDatabase.Contracts;

namespace CoinBank.Domain.Repositories.Contracts
{
    public interface ISwapRepository : IMonjoRepository<Swap>
    {
    }
}
