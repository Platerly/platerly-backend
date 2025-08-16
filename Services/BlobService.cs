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

        public async Task<string> UploadProfileImageAsync(IFormFile file, string fileName)
        {
            var blobClient = new BlobContainerClient(_connectionString, _containerName);
            await blobClient.CreateIfNotExistsAsync();

            var blob = blobClient.GetBlobClient(fileName);
            using (var stream = file.OpenReadStream())
            {
                await blob.UploadAsync(stream, overwrite: true);
            }

            return blob.Uri.ToString(); // Return public URL
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

        public async Task<string> UploadRecipeImageAsync(IFormFile file, string fileName)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient("recipe-images");
            var blobClient = containerClient.GetBlobClient(fileName);

            using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, overwrite: true);

            return blobClient.Uri.ToString();
        }

    }
}
