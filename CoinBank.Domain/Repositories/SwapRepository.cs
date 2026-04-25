using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using Utilities.MongoDatabase;
using Utilities.MongoDatabase.Contracts;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Domain.Repositories
{
    public class SwapRepository(IMonjoConnection connection) : MonjoRepository<Swap>(connection), ISwapRepository, ISingletonDependency
    {
    }
}
