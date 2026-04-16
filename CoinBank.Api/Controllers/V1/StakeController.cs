using Asp.Versioning;
using CoinBank.Services._Stake;
using CoinBank.Services._Stake.DTOs.Results;
using CoinBank.Services._Stake.DTOs.Updates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Utilities.Api;
using Utilities.Attributes;
using Utilities.Filters;

namespace CoinBank.Api.Controllers.V1
{
    [ApiController]
    [ApiResultFilter]
    [ApiVersion("1")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class StakeController(IStakeService _stakeService) : ApiBaseController
    {
       
        [HttpPost("[action]")]
        [Authorize(RequireActiveWithEVMWallet = true)]
        [CustomRateLimit(maxAttemptsCount: 20)]
        [SwaggerOperation(Summary = "Create stake", Tags = ["Stake"])]
        public async Task<StakeResult> CreateStakeAsync(CreateStakeUpdate update)
        {
            return await _stakeService.CreateStakeAsync(
                update,
                PublicKey,
                EVMWalletAddress
            );
        }

    
        [HttpPost("[action]")]
        [Authorize(RequireActiveUser = false)]
        [CustomRateLimit(maxAttemptsCount: 50)]
        [SwaggerOperation(Summary = "Get stake history", Tags = ["Stake"])]
        public async Task<StakeListResult> GetStakeHistoryAsync(StakeHistoryUpdate update)
        {
            return await _stakeService.GetStakeHistoryAsync(
                update,
                PublicKey,
                EVMWalletAddress
            );
        }

    
        [HttpPost("[action]")]
        [Authorize(RequireActiveUser = false)]
        [CustomRateLimit(maxAttemptsCount: 50)]
        [SwaggerOperation(Summary = "Get wallet stats", Tags = ["Stake"])]
        public async Task<List<StakeWalletStatsResult>> GetWalletStatsAsync(GetStakeWalletStatsUpdate update)
        {
            return await _stakeService.GetWalletStatsAsync(
                update,
                PublicKey,
                EVMWalletAddress
            );
        }

      
        [HttpPost("[action]")]
        [Authorize(RequireActiveUser = false)]
        [CustomRateLimit(maxAttemptsCount: 50)]
        [SwaggerOperation(Summary = "Get stake detail", Tags = ["Stake"])]
        public async Task<StakeDetailResult> GetStakeDetailAsync(StakeDetailUpdate update)
        {
            return await _stakeService.GetStakeDetailAsync(
                update,
                PublicKey,
                EVMWalletAddress
            );
        }
    }
}
 