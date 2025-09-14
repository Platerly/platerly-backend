using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Platerly.Api.Services
{
    public class BlobService : IBlobService
    {
        private readonly BlobServiceClient _svc;
        private readonly string _profileContainer;
        private readonly string _recipeContainer;
        private readonly string _cookbookContainer; // NEW

        public BlobService(IConfiguration configuration)
        {
            var conn = configuration["AzureStorage:ConnectionString"];
            _profileContainer = configuration["AzureStorage:ProfilePicturesContainer"] ?? "profile-pictures";
            _recipeContainer = configuration["AzureStorage:RecipeImagesContainer"] ?? "recipe-images";
            _cookbookContainer = configuration["AzureStorage:CookbookCoversContainer"] ?? "cookbook-covers"; // NEW

            if (string.IsNullOrWhiteSpace(conn))
                throw new InvalidOperationException("AzureStorage:ConnectionString is missing.");
            if (string.IsNullOrWhiteSpace(_profileContainer))
                throw new InvalidOperationException("AzureStorage:ProfilePicturesContainer is missing.");
            if (string.IsNullOrWhiteSpace(_recipeContainer))
                throw new InvalidOperationException("AzureStorage:RecipeImagesContainer is missing.");
            if (string.IsNullOrWhiteSpace(_cookbookContainer))
                throw new InvalidOperationException("AzureStorage:CookbookCoversContainer is missing."); // NEW

            _svc = new BlobServiceClient(conn);
        }

        private static BlobHttpHeaders ToHeaders(IFormFile file) => new()
        {
            ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType
        };

        // -------- Profile Images --------
        public async Task<string> UploadProfileImageAsync(IFormFile file, string fileName)
        {
            var container = _svc.GetBlobContainerClient(_profileContainer);
            await container.CreateIfNotExistsAsync(PublicAccessType.Blob);
            var blob = container.GetBlobClient(fileName);

            await using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = ToHeaders(file)
            });

            return blob.Uri.ToString();
        }

        public async Task<bool> DeleteProfileImageIfExistsAsync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            var container = _svc.GetBlobContainerClient(_profileContainer);
            var result = await container.DeleteBlobIfExistsAsync(fileName);
            return result.Value;
        }

        // -------- Recipe Images --------
        public async Task<string> UploadRecipeImageAsync(IFormFile file, string fileName)
        {
            var container = _svc.GetBlobContainerClient(_recipeContainer);
            await container.CreateIfNotExistsAsync(PublicAccessType.Blob);
            var blob = container.GetBlobClient(fileName);

            await using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = ToHeaders(file)
            });

            return blob.Uri.ToString();
        }

        public async Task<bool> DeleteRecipeImageIfExistsAsync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            var container = _svc.GetBlobContainerClient(_recipeContainer);
            var result = await container.DeleteBlobIfExistsAsync(fileName);
            return result.Value;
        }

        // -------- Cookbook Covers (NEW) --------
        public async Task<string> UploadCookbookCoverAsync(IFormFile file, string fileName)
        {
            var container = _svc.GetBlobContainerClient(_cookbookContainer);
            await container.CreateIfNotExistsAsync(PublicAccessType.Blob);
            var blob = container.GetBlobClient(fileName);

            await using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = ToHeaders(file)
            });

            return blob.Uri.ToString();
        }

        public async Task<bool> DeleteCookbookCoverIfExistsAsync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            var container = _svc.GetBlobContainerClient(_cookbookContainer);
            var result = await container.DeleteBlobIfExistsAsync(fileName);
            return result.Value;
        }
    }
}
