using Asp.Versioning;
using CoinBank.Services._Withdrawal;
using CoinBank.Services._Withdrawal.DTOs.Results;
using CoinBank.Services._Withdrawal.DTOs.Updates;
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
    public class WithdrawalController(IWithdrawalService _withdrawalService) : ApiBaseController
    {

        [HttpPost("[action]")]
        [Authorize(RequireActiveWithEVMWallet = true)]
        [CustomRateLimit(maxAttemptsCount: 20)]
        [SwaggerOperation(Summary = "Withdraw stake profit", Tags = ["Withdrawal"])]
        public async Task<WithdrawalResult> WithdrawStakeProfitAsync(
            WithdrawStakeProfitUpdate update)
        {
            return await _withdrawalService.WithdrawStakeProfitAsync(
                update,
                PublicKey,
                EVMWalletAddress
            );
        }

        [HttpPost("[action]")]
        [Authorize(RequireActiveWithEVMWallet = true)]
        [CustomRateLimit(maxAttemptsCount: 20)]
        [SwaggerOperation(Summary = "Withdraw stake amount", Tags = ["Withdrawal"])]
        public async Task<WithdrawalResult> WithdrawStakeAmountAsync(
            WithdrawStakeAmountUpdate update)
        {
            return await _withdrawalService.WithdrawStakeAmountAsync(
                update,
                PublicKey,
                EVMWalletAddress
            );
        }


    }
}
