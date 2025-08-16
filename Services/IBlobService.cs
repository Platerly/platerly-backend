namespace Platerly.Api.Services
{
    public interface IBlobService
    {
        Task<string> UploadProfileImageAsync(IFormFile file, string filename);
        Task<string> UploadRecipeImageAsync(IFormFile file, string fileName);

        Task<bool> DeleteProfileImageIfExistsAsync(string fileName);
        Task<bool> DeleteRecipeImageIfExistsAsync(string fileName);

    }
}
