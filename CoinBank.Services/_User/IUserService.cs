using CoinBank.Services._User.DTOs.Results;
using CoinBank.Services._User.DTOs.Updates;
using Microsoft.AspNetCore.Mvc;

namespace CoinBank.Services._User
{
    public interface IUserService
    {

        //auth 
        NonceResult GetNonce(NonceRequest update, string ip);
        Task<ActionResult> GetToken(NonceVerification update, string ip);
        Task<GetUserResult> GetUserAsync(string whois);
        Task<ActionResult> GetTokenWithPureWalletAddress(GetTokenWithPureWalletAddress update, string ip);
        Task<GetUserStatsResult> GetUserStatsAsync(string whois); 
    }
}
 