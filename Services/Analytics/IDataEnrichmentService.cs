using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    public interface IDataEnrichmentService
    {
        /// <summary>
        /// Enrich ingredient names with allergens, categories, dietary flags, and substitutes
        /// </summary>
        Task<IngredientEnrichmentResponse> EnrichIngredientsAsync(
            IngredientEnrichmentRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Enrich a recipe with dietary tags, allergen warnings, and metadata
        /// </summary>
        Task<RecipeEnrichmentResponse> EnrichRecipeAsync(
            RecipeEnrichmentRequest request,
            CancellationToken ct = default);
    }
}
