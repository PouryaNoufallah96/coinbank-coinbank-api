using Asp.Versioning;
using CoinBank.Services._Price.DTOs.Storages;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Collections.Concurrent;
using Utilities.Api;
using Utilities.Attributes;
using Utilities.Filters;

namespace CoinBank.Api.Controllers.V1
{
    [ApiController]
    [ApiResultFilter]
    [ApiVersion("1")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class PriceController(CoinHistoryStorage _coinHistoryStorage) : ApiBaseController
    {

        [HttpGet("[action]")]
        [CustomRateLimit(maxAttemptsCount: 50)]
        [SwaggerOperation(Summary = "For getting ohclv History", Tags = ["Landing"])]
        public ConcurrentDictionary<string, CoinHistoryData> GetOHCLVHistory()
        {
            return _coinHistoryStorage;
        }
    }
}
 