using Asp.Versioning;
using CoinBank.Services._PreSaleOrder;
using CoinBank.Services._PreSaleOrder.DTOs.Results;
using CoinBank.Services._PreSaleOrder.DTOs.Updates;
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
    public class PreSaleOrderController(IPreSaleOrderService _preSaleOrderService) : ApiBaseController
    {

       
        [HttpPost("[action]")]
        [Authorize(RequireActiveWithEVMWallet = true)]
        [CustomRateLimit(maxAttemptsCount: 20)]
        [SwaggerOperation(Summary = "Create pre-sale order", Tags = ["PreSaleOrder"])]
        public async Task<PreSaleOrderResult> CreatePreSaleOrderAsync(CreatePreSaleOrderUpdate update)
        {
            return await _preSaleOrderService.CreatePreSaleOrderAsync(
                update,
                PublicKey,
                EVMWalletAddress
            );
        }

      
        [HttpPost("[action]")]
        [Authorize(RequireActiveUser = false)]
        [CustomRateLimit(maxAttemptsCount: 50)]
        [SwaggerOperation(Summary = "Get pre-sale order history", Tags = ["PreSaleOrder"])]
        public async Task<PreSaleOrderListResult> GetPreSaleOrderHistoryAsync(GetPreSaleOrderHistoryUpdate update)
        {
            return await _preSaleOrderService.GetPreSaleOrderHistoryAsync(
                update,
                PublicKey,
                EVMWalletAddress
            );
        }

      
        [HttpPost("[action]")]
        [Authorize(RequireActiveUser = false)]
        [CustomRateLimit(maxAttemptsCount: 50)]
        [SwaggerOperation(Summary = "Get wallet stats", Tags = ["PreSaleOrder"])]
        public async Task<List<PreSaleOrderWalletStatsResult>> GetWalletStatsAsync(GetPreSaleOrderWalletStatsUpdate update)
        {
            return await _preSaleOrderService.GetWalletStatsAsync(
                update,
                PublicKey,
                EVMWalletAddress
            );
        }

      
        [HttpPost("[action]")]
        [Authorize(RequireActiveUser = false)]
        [CustomRateLimit(maxAttemptsCount: 50)]
        [SwaggerOperation(Summary = "Get One pre-sale order detail", Tags = ["PreSaleOrder"])]
        public async Task<PreSaleOrderDetailResult> GetOnePreSaleOrderDetailAsync(GetOnePreSaleOrderDetailUpdate update)
        {
            return await _preSaleOrderService.GetOnePreSaleOrderDetailAsync(
                update,
                PublicKey,
                EVMWalletAddress
            );
        }
    }
}
