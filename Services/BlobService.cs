using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Platerly.Api.Services
{
    public class BlobService : IBlobService
    {
        private readonly string _connectionString;
        private readonly string _containerName;
        private readonly BlobServiceClient _blobServiceClient;

        public BlobService(IConfiguration configuration)
        {
            _connectionString = configuration["AzureStorage:ConnectionString"]!;
            _containerName = configuration["AzureStorage:ContainerName"]!;
            _blobServiceClient = new BlobServiceClient(_connectionString);
        }

        // In UploadProfileImageAsync: set content type and reuse the service client
        public async Task<string> UploadProfileImageAsync(IFormFile file, string fileName)
        {
            var container = _blobServiceClient.GetBlobContainerClient(_containerName);
            await container.CreateIfNotExistsAsync();
            var blob = container.GetBlobClient(fileName);

            using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, new Azure.Storage.Blobs.Models.BlobUploadOptions
            {
                HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
                {
                    ContentType = file.ContentType
                }
            }, cancellationToken: default);

            return blob.Uri.ToString();
        }

        // In UploadRecipeImageAsync: ensure container exists like you do for profile
        public async Task<string> UploadRecipeImageAsync(IFormFile file, string fileName)
        {
            var container = _blobServiceClient.GetBlobContainerClient("recipe-images");
            await container.CreateIfNotExistsAsync();
            var blob = container.GetBlobClient(fileName);

            using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, overwrite: true);

            return blob.Uri.ToString();
        }


        public async Task<bool> DeleteProfileImageIfExistsAsync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            var container = _blobServiceClient.GetBlobContainerClient(_containerName);
            var result = await container.DeleteBlobIfExistsAsync(fileName);
            return result.Value;
        }

        public async Task<bool> DeleteRecipeImageIfExistsAsync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            var container = _blobServiceClient.GetBlobContainerClient("recipe-images");
            var result = await container.DeleteBlobIfExistsAsync(fileName);
            return result.Value;
        }


    }
}
