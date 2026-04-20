using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using Utilities.MongoDatabase;
using Utilities.MongoDatabase.Contracts;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Domain.Repositories
{
    public class TransactionLogRepository(IMonjoConnection connection) : MonjoRepository<TransactionLog>(connection), ITransactionLogRepository, ISingletonDependency
    {
    }
}
