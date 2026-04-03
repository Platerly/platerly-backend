using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>Provides B2B sustainability scoring for recipes and multi-recipe comparisons based on ingredient environmental data.</summary>
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
