using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Platerly.Api.Services
{
    /// <summary>
    /// Infrastructure service — manages file uploads and blob access via Azure Blob Storage.
    /// Depends on <c>AzureStorage:ConnectionString</c> from configuration (throws at startup if absent).
    ///
    /// Four containers (names configurable via app settings, with defaults):
    /// <list type="bullet">
    ///   <item><c>profile-pictures</c> — user profile images</item>
    ///   <item><c>recipe-images</c> — recipe photo uploads</item>
    ///   <item><c>cookbook-covers</c> — cookbook cover images</item>
    ///   <item><c>profile-banner</c> — user profile banner images</item>
    /// </list>
    ///
    /// All containers are created with <c>PublicAccessType.None</c> — blobs are not publicly
    /// accessible. Access is granted via SAS URIs with a 10-year expiry (effectively permanent
    /// URLs for CDN caching). The <c>BlobClient</c> must have an account-key connection string to
    /// generate SAS URIs; managed-identity clients cannot call <c>GenerateSasUri</c>.
    /// </summary>
    public class BlobService : IBlobService
    {
        private readonly BlobServiceClient _svc;
        private readonly string _profileContainer;
        private readonly string _recipeContainer;
        private readonly string _cookbookContainer;
        private readonly string _bannerContainer;

        public BlobService(IConfiguration configuration)
        {
            var conn = configuration["AzureStorage:ConnectionString"];
            _profileContainer = configuration["AzureStorage:ProfilePicturesContainer"] ?? "profile-pictures";
            _recipeContainer = configuration["AzureStorage:RecipeImagesContainer"] ?? "recipe-images";
            _cookbookContainer = configuration["AzureStorage:CookbookCoversContainer"] ?? "cookbook-covers";
            _bannerContainer = configuration["AzureStorage:BannerContainerName"] ?? "profile-banner";

            if (string.IsNullOrWhiteSpace(conn))
                throw new InvalidOperationException("AzureStorage:ConnectionString is missing.");
            if (string.IsNullOrWhiteSpace(_profileContainer))
                throw new InvalidOperationException("AzureStorage:ProfilePicturesContainer is missing.");
            if (string.IsNullOrWhiteSpace(_recipeContainer))
                throw new InvalidOperationException("AzureStorage:RecipeImagesContainer is missing.");
            if (string.IsNullOrWhiteSpace(_cookbookContainer))
                throw new InvalidOperationException("AzureStorage:CookbookCoversContainer is missing.");
            if (string.IsNullOrWhiteSpace(_bannerContainer))
                throw new InvalidOperationException("AzureStorage:BannerContainerName is missing.");

            _svc = new BlobServiceClient(conn);
        }

        private static BlobHttpHeaders ToHeaders(IFormFile file) => new()
        {
            ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType
        };

        /// <summary>
        /// Generates a read-only SAS URI for a blob with long expiry.
        /// This allows controlled access without making containers public.
        /// </summary>
        private static string GenerateSasUri(BlobClient blob)
        {
            if (!blob.CanGenerateSasUri)
                throw new InvalidOperationException("BlobClient cannot generate SAS URI. Ensure connection string includes account key.");

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = blob.BlobContainerName,
                BlobName = blob.Name,
                Resource = "b", // b = blob
                ExpiresOn = DateTimeOffset.UtcNow.AddYears(10) // Long expiry for permanent URLs
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            return blob.GenerateSasUri(sasBuilder).ToString();
        }

        // -------- Profile Images --------
        public async Task<string> UploadProfileImageAsync(IFormFile file, string fileName)
        {
            var container = _svc.GetBlobContainerClient(_profileContainer);
            await container.CreateIfNotExistsAsync(PublicAccessType.None);
            var blob = container.GetBlobClient(fileName);

            await using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = ToHeaders(file)
            });

            return GenerateSasUri(blob);
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
            await container.CreateIfNotExistsAsync(PublicAccessType.None);
            var blob = container.GetBlobClient(fileName);

            await using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = ToHeaders(file)
            });

            return GenerateSasUri(blob);
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
            await container.CreateIfNotExistsAsync(PublicAccessType.None);
            var blob = container.GetBlobClient(fileName);

            await using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = ToHeaders(file)
            });

            return GenerateSasUri(blob);
        }

        public async Task<bool> DeleteCookbookCoverIfExistsAsync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            var container = _svc.GetBlobContainerClient(_cookbookContainer);
            var result = await container.DeleteBlobIfExistsAsync(fileName);
            return result.Value;
        }

        // -------- Banner Images (NEW) --------
        public async Task<string> UploadBannerImageAsync(IFormFile file, string fileName)
        {
            var container = _svc.GetBlobContainerClient(_bannerContainer);
            await container.CreateIfNotExistsAsync(PublicAccessType.None);
            var blob = container.GetBlobClient(fileName);

            await using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = ToHeaders(file)
            });

            return GenerateSasUri(blob);
        }

        public async Task<bool> DeleteBannerImageIfExistsAsync(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            var container = _svc.GetBlobContainerClient(_bannerContainer);
            var result = await container.DeleteBlobIfExistsAsync(fileName);
            return result.Value;
        }

        // -------- SAS URL Generation (for migration) --------

        /// <summary>
        /// Helper to generate SAS URL for an existing blob by name.
        /// Returns null if blob doesn't exist.
        /// </summary>
        private async Task<string?> GenerateSasUrlForBlob(string containerName, string blobName)
        {
            if (string.IsNullOrWhiteSpace(blobName)) return null;

            var container = _svc.GetBlobContainerClient(containerName);
            var blob = container.GetBlobClient(blobName);

            // Check if blob exists before generating SAS
            var exists = await blob.ExistsAsync();
            if (!exists.Value) return null;

            return GenerateSasUri(blob);
        }

        public Task<string?> GenerateProfileImageSasUrl(string blobName)
            => GenerateSasUrlForBlob(_profileContainer, blobName);

        public Task<string?> GenerateRecipeImageSasUrl(string blobName)
            => GenerateSasUrlForBlob(_recipeContainer, blobName);

        public Task<string?> GenerateCookbookCoverSasUrl(string blobName)
            => GenerateSasUrlForBlob(_cookbookContainer, blobName);

        public Task<string?> GenerateBannerImageSasUrl(string blobName)
            => GenerateSasUrlForBlob(_bannerContainer, blobName);
    }
}
