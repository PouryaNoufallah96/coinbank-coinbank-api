using Asp.Versioning;
using CoinBank.Services._File;
using Microsoft.AspNetCore.Mvc;
using Utilities.Api;
using Utilities.Attributes;
using Utilities.Filters;
using Utilities.Permissions;

namespace CoinHalls.Api.Controllers.V1
{

    [ApiController]
    [ApiResultFilter]
    [ApiVersion("1")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class FileController(IFileService _fileService) : ApiBaseController
    {
        [HttpGet("[action]/{fileName}")]
        //[IgnoreSignatureAttribute]
        public async Task<IActionResult> DownloadFileAsync([FromRoute] string fileName)
        {
            var memory = await _fileService.GetFileAsync(fileName);
            var fileSuffix = fileName.Split(".").LastOrDefault();
            var fileMediaType = fileSuffix?.ToLower() == "png" || fileSuffix?.ToLower() == "jpg" || fileSuffix?.ToLower() == "jpeg" ? "image" : "application";
            return fileMediaType == "image" ? File(memory, $"{fileMediaType}/{fileSuffix}") : File(memory, $"{fileMediaType}/{fileSuffix}", fileName);
        }

        [HttpPost("[action]")]
        [FileSizeLimit(15 * 1024 * 1024)]
        [Security(disable: true)]
        [Authorize(Permissions.PreSaleManage)]
        public async Task<string> UploadFileAsync([FromQuery] string fileName, IFormFile file)
        {
            //if (WalletAddress.ToLower() != "admin wallet") throw new BadRequestException("you can not upload game image");
            return await _fileService.UploadFileAsync(fileName, file);
        }


        [HttpDelete("[action]")]
        [Authorize(Permissions.PreSaleManage)]
        public bool Deletefile([FromRoute] string filename)
        {
            //if (walletaddress.tolower() != "admin wallet") throw new badrequestexception("you can not remove game image");
            return _fileService.DeleteFile(filename);
        }
    }
}
