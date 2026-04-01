namespace CoinBank.Services._User.DTOs.Results
{
    public class GetUserResult
    {
        public DateTime CreateMoment { get; set; }
        public string EVMWalletAddress { get; set; }
        public string TronWalletAddress { get; set; }
        public ICollection<DateTime> LoginHistories { get; set; } 
    }
} 
 