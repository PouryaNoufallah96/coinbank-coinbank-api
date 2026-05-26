using Asp.Versioning;
using CoinBank.Services._BlockChain;
using CoinBank.Services._BlockChain.DTOs.Updates;
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
        //[HttpPost("[action]")]
        //[CustomRateLimit(maxAttemptsCount: 20)]
        //[SwaggerOperation(Summary = "test swap", Tags = ["Swap"])]
        //public async Task<string> Test(CreateSwapUpdate update)
        //{
        //    var (fee, feeToken) = await _blockChainService.SwapGetEstimatedFeeAsync(new GetSwapEstimatedFeeUpdate
        //    {
        //        SwapReference = "0xe60196a153cf8162224d5491b04219e59d883e72d4b302da478c4c3179e79bca",
        //        DstEid = 30102,
        //        SourceNetwork = "BEP20",
        //        SourceTokenAddress = "0xd82895730d46e5671Aa9d7660f75E0AD3beE736F",
        //        SourceAmoutInWei = new System.Numerics.BigInteger(1000000000),
        //        DestinationNetwork = "ERC20",
        //        DestinationTokenAddress = "0xd82895730d46e5671Aa9d7660f75E0AD3beE736F",
        //        DestinationWallet = "0x798457be80878b1f132e3A516b4b44E197CE3076",
        //    }); 

        //    var destinationTokenOutAmountInWei = await _blockChainService.SwapGetOutputAmountAsync(new SwapGetOutputAmount
        //    {
        //        DestinationTokenAddress = "0xd82895730d46e5671Aa9d7660f75E0AD3beE736F",
        //        SourceTokenAddress = "0xd82895730d46e5671Aa9d7660f75E0AD3beE736F",
        //        SourceAmountInWei = new System.Numerics.BigInteger(1000000000),
        //    });
        //    return "test";
        //} 


        [HttpPost("[action]")]
        [Authorize(RequireActiveUser = true)]
        [CustomRateLimit(maxAttemptsCount: 20)]
        [SwaggerOperation(Summary = "Create swap", Tags = ["Swap"])]
        public async Task<SwapCreatedResult> CreateSwapAsync(CreateSwapUpdate update)
        {
            return await _swapService.CreateSwapAsync(
                update,
                WalletAddress,
                NetworkType,
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
                WalletAddress,
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
                WalletAddress,
                PublicKey
            );
        }

    }
}
