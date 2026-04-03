namespace Platerly.Api.Services
{
    /// <summary>Manages image uploads and SAS URL generation for Azure Blob Storage across four containers (profiles, recipes, cookbooks, banners).</summary>
    public interface IBlobService
    {
        Task<string> UploadProfileImageAsync(IFormFile file, string filename);
        Task<string> UploadRecipeImageAsync(IFormFile file, string fileName);

        Task<bool> DeleteProfileImageIfExistsAsync(string fileName);
        Task<bool> DeleteRecipeImageIfExistsAsync(string fileName);

        Task<string> UploadCookbookCoverAsync(IFormFile file, string fileName);
        Task<bool> DeleteCookbookCoverIfExistsAsync(string fileName);

        Task<string> UploadBannerImageAsync(IFormFile file, string fileName);
        Task<bool> DeleteBannerImageIfExistsAsync(string fileName);

        // SAS URL generation for migration
        Task<string?> GenerateProfileImageSasUrl(string blobName);
        Task<string?> GenerateRecipeImageSasUrl(string blobName);
        Task<string?> GenerateCookbookCoverSasUrl(string blobName);
        Task<string?> GenerateBannerImageSasUrl(string blobName);
    }
}
