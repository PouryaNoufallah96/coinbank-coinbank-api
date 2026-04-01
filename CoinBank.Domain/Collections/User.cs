using MongoDB.Bson.Serialization.Attributes;
using Utilities.Attributes;
using Utilities.MongoDatabase.Documents;

namespace CoinBank.Domain.Collections
{
    [MonjoCollectionName("Users")]
    public class User : BaseDocument
    {
        public string UserPublicKey { get; set; } = Guid.NewGuid().ToString("N");
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
        public UserRole Role { get; set; }
        public List<DateTime> LoginDates { get; set; } = [];

        public string EVMWalletAddress { get; set; } // use for bep20, erc20 , polygon, Arbitrum
        public string TronWalletAddress { get; set; }  // for trc20


        public string UserName { get; set; } = null;// user for admin
        public string PasswordHash { get; set; } = null;// use for admin
        public List<string> Permissions { get; set; } = null; // for admin
        [BsonDefaultValue(UserStatus.NotVerified)] public UserStatus Status { get; set; }
    }

    public enum UserStatus { Active, Ban, Archived, NotVerified }
    public enum UserRole { Customer, Admin }
}
