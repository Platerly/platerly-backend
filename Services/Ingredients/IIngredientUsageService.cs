using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    public interface IIngredientUsageService
    {
        /// <summary>
        /// Log ingredient usage when a recipe is created
        /// </summary>
        Task LogRecipeIngredientsAsync(
            Guid recipeId,
            Guid userId,
            IEnumerable<Ingredient> ingredients,
            string? ipAddress = null,
            string? userAgent = null,
            CancellationToken ct = default);

        /// <summary>
        /// Log a single ingredient usage
        /// </summary>
        Task LogIngredientUsageAsync(
            Guid globalIngredientId,
            Guid recipeId,
            Guid userId,
            string? quantity = null,
            string? unit = null,
            string? region = null,
            string? countryCode = null,
            string? city = null,
            string? dietaryTags = null,
            CancellationToken ct = default);

        /// <summary>
        /// Log a search query for demand signals
        /// </summary>
        Task LogSearchAsync(
            string query,
            int resultCount,
            string? region = null,
            string? searchContext = null,
            CancellationToken ct = default);

        /// <summary>
        /// Recompute daily trends (called by background service)
        /// </summary>
        Task RecomputeDailyTrendsAsync(DateOnly date, CancellationToken ct = default);

        /// <summary>
        /// Recompute regional stats (called by background service)
        /// </summary>
        Task RecomputeRegionalStatsAsync(string region, CancellationToken ct = default);

        /// <summary>
        /// Update ingredient pairing data based on recipe co-occurrence
        /// </summary>
        Task UpdatePairingsForRecipeAsync(
            Guid recipeId,
            IEnumerable<Guid> ingredientIds,
            CancellationToken ct = default);
    }
}
