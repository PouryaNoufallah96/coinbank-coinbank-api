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
        [CustomRateLimit]
        [Authorize(RequireActiveWithBEP20Wallet = true)]
        [SwaggerOperation(Summary = "Create Pending Stake ", Tags = ["Stake"])]
        public async Task<StakeResult> CreateStakeAsync(CreateStakeUpdate update)
        {
            return await _stakeService.CreateStakeAsync(
                update,
                EVMWalletAddress,
                NetworkType
            );
        }


        [HttpPost("[action]")]
        [CustomRateLimit]
        [Authorize(RequireActiveUser = false, JustBSC = true)]
        [SwaggerOperation(Summary = "Get stake history", Tags = ["Stake"])]
        public async Task<StakeListResult> GetStakeHistoryAsync(StakeHistoryUpdate update)
        {
            return await _stakeService.GetStakeHistoryAsync(
                update,
                EVMWalletAddress
            );
        }

        [HttpPost("[action]")]
        [CustomRateLimit]
        [Authorize(RequireActiveUser = false, JustBSC = true)]
        [SwaggerOperation(Summary = "Get one stake Detail", Tags = ["Stake"])]
        public async Task<StakeDetailResult> GetStakeDetailAsync(StakeDetailUpdate update)
        {
            return await _stakeService.GetStakeDetailAsync(
                update,
                EVMWalletAddress
            );
        }

        [HttpPost("[action]")]
        [CustomRateLimit]
        [Authorize(RequireActiveUser = false, JustBSC = true)]
        [SwaggerOperation(Summary = "Get Wallet stats for stake side", Tags = ["Stake"])]
        public async Task<List<StakeWalletStatsResult>> GetWalletStatsAsync(GetStakeWalletStatsUpdate update)
        {
            return await _stakeService.GetWalletStatsAsync(
                update, 
                EVMWalletAddress
            );
        }
    }
}
 