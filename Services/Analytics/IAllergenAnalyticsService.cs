using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>Provides B2B allergen analytics: prevalence across the user base, per-allergen ingredient queries, and safe-ingredient lookups.</summary>
    public interface IAllergenAnalyticsService
    {
        /// <summary>
        /// Get allergen prevalence across the user base
        /// </summary>
        Task<AllergenPrevalenceResponse> GetPrevalenceAsync(
            CancellationToken ct = default);

        /// <summary>
        /// Get ingredients flagged for a specific allergen type
        /// </summary>
        Task<AllergenIngredientsResponse> GetIngredientsByAllergenAsync(
            AllergenIngredientsRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Get ingredients confirmed safe for a set of allergens
        /// </summary>
        Task<SafeIngredientsResponse> GetSafeIngredientsAsync(
            SafeIngredientsRequest request,
            CancellationToken ct = default);
    }
}
