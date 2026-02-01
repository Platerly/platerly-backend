using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Nutrition
{
    public interface INutritionService
    {
        /// <summary>
        /// Get estimated nutrition for a recipe by ID
        /// </summary>
        Task<RecipeNutritionResponse?> GetRecipeNutritionAsync(
            Guid recipeId,
            CancellationToken ct = default);

        /// <summary>
        /// Estimate nutrition from a list of ingredients
        /// </summary>
        Task<NutritionEstimateResponse> EstimateNutritionAsync(
            NutritionEstimateRequest request,
            CancellationToken ct = default);
    }
}
