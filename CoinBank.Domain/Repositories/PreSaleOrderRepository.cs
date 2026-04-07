using CoinBank.Domain.Collections;
using CoinBank.Domain.Repositories.Contracts;
using Utilities.MongoDatabase;
using Utilities.MongoDatabase.Contracts;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Domain.Repositories
{
    public class PreSaleOrderRepository(IMonjoConnection connection) : 
        MonjoRepository<PreSaleOrder>(connection), IPreSaleOrderRepository, ISingletonDependency
    {
    }
}
