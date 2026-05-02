using Asp.Versioning;
using CoinBank.Services._Swap;
using CoinBank.Services._Swap.DTOs.Results;
using CoinBank.Services._Swap.DTOs.Updates;
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
    public class SwapController(ISwapService _swapService) : ApiBaseController
    {
        [HttpPost("[action]")]
        [Authorize(RequireActiveUser = true)]
        [CustomRateLimit(maxAttemptsCount: 20)]
        [SwaggerOperation(Summary = "Create swap", Tags = ["Swap"])]
        public async Task<SwapCreatedResult> CreateSwapAsync(CreateSwapUpdate update)
        {
            return await _swapService.CreateSwapAsync(
                update,
                EVMWalletAddress,
                TronWalletAddress,
                PublicKey
            );
        }

        [HttpPost("[action]")]
        [Authorize(RequireActiveUser = false)]
        [CustomRateLimit(maxAttemptsCount: 50)]
        [SwaggerOperation(Summary = "Get swap history", Tags = ["Swap"])]
        public async Task<SwapListResult> GetSwapHistoryAsync(SwapHistoryUpdate update)
        {
            return await _swapService.GetSwapHistoryAsync(
                update,
                EVMWalletAddress,
                TronWalletAddress,
                PublicKey
            );
        }

        [HttpPost("[action]")]
        [Authorize(RequireActiveUser = false)]
        [CustomRateLimit(maxAttemptsCount: 50)]
        [SwaggerOperation(Summary = "Get one swap by reference", Tags = ["Swap"])]
        public async Task<SwapResult> GetOneSwapByReferenceAsync(SwapReferenceUpdate update)
        {
            return await _swapService.GetOneSwapByReferenceAsync(
                update,
                EVMWalletAddress,
                TronWalletAddress,
                PublicKey
            );
        }

    }
}
