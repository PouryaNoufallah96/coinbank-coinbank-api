using Asp.Versioning;
using CoinBank.Services._PreSale;
using CoinBank.Services._PreSale.DTOs.Results;
using CoinBank.Services._PreSale.DTOs.Updates;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Utilities.Api;
using Utilities.Attributes;
using Utilities.Filters;
using Utilities.Permissions;

namespace CoinBank.Api.Controllers.V1
{

    [ApiController]
    [ApiResultFilter]
    [ApiVersion("1")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class PreSaleController(IPreSaleService _preSaleService) : ApiBaseController
    {

        [HttpPost("[action]")]
        [Authorize(Permissions.PreSaleManage)]
        [CustomRateLimit(maxAttemptsCount: 20)]
        [SwaggerOperation(Summary = "Create new pre-sale token", Tags = ["PreSale-Admin"])]
        public async Task<PreSaleResult> CreatePreSaleTokenAsync(CreatePreSaleTokenUpdate update)
        {
            return await _preSaleService.CreatePreSaleTokenAsync(update);
        }

        [HttpGet("[action]")]
        [CustomRateLimit(maxAttemptsCount: 50)]
        [Authorize(RequireActiveUser = false)]
        [SwaggerOperation(Summary = "user pre sale stats for merge with the hub data", Tags = ["PreSale"])]
        public async Task<List<PreSaleUserStatResult>> GetUserPreSaleStatsAsync()
        {
            return await _preSaleService.GetUserPreSaleStatsAsync(PublicKey, EVMWalletAddress);
        }


        ////global
        //[HttpPost("[action]")]
        //[CustomRateLimit(maxAttemptsCount: 50)]
        //[Authorize(RequireActiveUser = false)]
        //[SwaggerOperation(Summary = "Get all pre-sale tokens", Tags = ["PreSale"])]
        //public async Task<PreSaleListResult> GetAllPreSaleTokensAsync(Pagination pagination)
        //{
        //    return await _preSaleService.GetAllPreSaleTokensAsync(pagination);
        //}


        //[HttpPost("[action]")]
        //[CustomRateLimit(maxAttemptsCount: 50)]
        //[Authorize(RequireActiveUser = false)]
        //[SwaggerOperation(Summary = "Get one pre-sale token", Tags = ["PreSale"])]
        //public async Task<PreSaleResult> GetOnePreSaleTokenAsync(GetOnePreSaleTokenUpdate update)
        //{
        //    return await _preSaleService.GetOnePreSaleTokenAsync(update);
        //}

    }


}
