namespace CoinBank.Services._User.DTOs.Results
{
    public class GetUserResult
    {
        public DateTime CreateMoment { get; set; }
        public string EVMWalletAddress { get; set; }
        public string TronWalletAddress { get; set; }
        public ICollection<DateTime> LoginHistories { get; set; } 
    }


    public class TronVerificationServiceResponse
    {
        public bool valid { get; set; }
        public string recoveredAddress { get; set; }
        public string error { get; set; }
        public string details { get; set; }
    }
} 
 