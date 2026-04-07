using Microsoft.AspNetCore.Http;

namespace CoinBank.Services._File
{
    public interface IFileService
    {
        Task<MemoryStream> GetFileAsync(string fileName);

        Task<string> UploadFileAsync(string fileName, IFormFile file);

        bool DeleteFile(string fileName);
    }
}
