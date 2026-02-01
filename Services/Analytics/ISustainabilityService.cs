using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    public interface ISustainabilityService
    {
        Task<RecipeSustainabilityResponse?> GetRecipeSustainabilityAsync(
            Guid recipeId,
            CancellationToken ct = default);

        Task<SustainabilityCompareResponse> CompareRecipesAsync(
            SustainabilityCompareRequest request,
            CancellationToken ct = default);
    }
}
