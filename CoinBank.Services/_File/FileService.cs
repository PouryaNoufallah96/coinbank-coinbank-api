using CoinBank.Services._File.DTOs.Settings;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using Utilities.Exceptions.Common;
using Utilities.Services.Contracts;
using static Utilities.Constants.RegisterMode;

namespace CoinBank.Services._File
{

    public class FileService(
       FileSettings _fileSettings,
       IRandomService _randomService)
       : IFileService, ISingletonDependency
    {
        private readonly long _maxImageSize = 10 * 1024 * 1024;   // 5 MB
        private readonly long _maxDocSize = 50 * 1024 * 1024;    // 50 MB
        private readonly List<string> _allowedFormats = ["jpg", "jpeg", "png", "webp"];

        /// <summary>
        /// Retrieves a file from the specified path as a MemoryStream, ensuring the file exists and is accessible.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        /// <exception cref="BadRequestException"></exception>
        /// <exception cref="BaseException"></exception>
        public async Task<MemoryStream> GetFileAsync(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                    throw new BadRequestException("File name is required.");

                var dir = Directory.CreateDirectory(_fileSettings.ImagesPath);
                var fullPath = Path.Combine(dir.FullName, fileName);

                if (!File.Exists(fullPath))
                    throw new BadRequestException("File not found");

                MemoryStream memory = new();
                using (FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read))
                    await stream.CopyToAsync(memory);

                memory.Position = 0;
                return memory;
            }
            catch (BadRequestException ex)
            {
                throw new BadRequestException(ex.Message);
            }
            catch (Exception ex)
            {
                throw new BaseException(ex.Message);
            }
        }

        /// <summary>
        /// Uploads a file (image or document) to storage with validation for allowed formats and size limits,
        /// sanitizes the filename, generates a unique name, processes images to WebP format, and saves the file.
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="file"></param>
        /// <returns></returns>
        /// <exception cref="BadRequestException"></exception>
        /// <exception cref="BaseException"></exception>
        public async Task<string> UploadFileAsync(string fileName, IFormFile file)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                    throw new BadRequestException("file name is required");

                if (file == null || file.Length == 0)
                    throw new BadRequestException("there is no file for upload.");

                var extension = Path.GetExtension(file.FileName)
                                    .TrimStart('.')
                                    .ToUpperInvariant();

                if (!_allowedFormats.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    throw new BadRequestException($"File type not supported : {extension}");

                if (!Directory.Exists(_fileSettings.ImagesPath))
                    Directory.CreateDirectory(_fileSettings.ImagesPath);

                var path = Path.IsPathRooted(_fileSettings.ImagesPath)
                    ? _fileSettings.ImagesPath
                    : Path.Combine(Directory.GetCurrentDirectory(), _fileSettings.ImagesPath);

                var dir = new DirectoryInfo(path);

                // Sanitize provided name
                fileName = Path.GetFileNameWithoutExtension(fileName);

                // Unique filename
                var timeStamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssffff");
                var rand = _randomService.GetSecureNumericString(5);
                var storedName = $"{fileName}-{timeStamp}{rand}.{extension.ToLower()}";
                var fullPath = Path.Combine(dir.FullName, storedName);

                // Handle image separately
                var imageExtensions = new[] { "PNG", "JPG", "JPEG", "WEBP" };

                if (imageExtensions.Contains(extension))
                {
                    if (file.Length > _maxImageSize)
                        throw new BadRequestException($"maximum of image size is {_maxImageSize} mg");

                    using var image = Image.Load(file.OpenReadStream());

                    // Optional resize uncomment if needed
                    // image.Mutate(x => x.Resize(new ResizeOptions
                    // {
                    //     Mode = ResizeMode.Max,
                    //     Size = new Size(1024, 1024)
                    // }));

                    image.Metadata.ExifProfile = null; // remove sensitive EXIF
                    var webpEncoder = new WebpEncoder { Quality = 80 };

                    await image.SaveAsync(fullPath, webpEncoder);
                }
                else
                {
                    throw new BadRequestException("file type is not supported");                   
                }

                return storedName;
            }
            catch (Exception ex)
            {
                throw new BaseException("there is a problem in uploding file" + ex.Message);
            }
        }

        /// <summary>
        /// Deletes a specified file from storage if it exists, returning true on success.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        /// <exception cref="BadRequestException"></exception>
        /// <exception cref="BaseException"></exception>
        public bool DeleteFile(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                    throw new BadRequestException("file name is required");

                var dir = Directory.CreateDirectory(_fileSettings.ImagesPath);
                var fullPath = Path.Combine(dir.FullName, fileName);

                if (File.Exists(fullPath))
                    File.Delete(fullPath);

                return true;
            }
            catch (BadRequestException ex)
            {
                throw new BadRequestException(ex.Message);
            }
            catch (Exception ex)
            {
                throw new BaseException(ex.Message);
            }
        }
    }
}

