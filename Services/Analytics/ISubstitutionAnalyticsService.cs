using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>Provides B2B substitution usage analytics: top substitutions, per-ingredient substitutes, and reason breakdowns.</summary>
    public interface ISubstitutionAnalyticsService
    {
        /// <summary>
        /// Get top substitutions by usage count, filterable by reason and allergy
        /// </summary>
        Task<TopSubstitutionsResponse> GetTopSubstitutionsAsync(
            TopSubstitutionsRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Get all substitutes for a specific ingredient with confidence scores and votes
        /// </summary>
        Task<IngredientSubstitutionsResponse> GetSubstitutionsForIngredientAsync(
            IngredientSubstitutionsRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Get breakdown by substitution reason — counts, percentages, avg confidence
        /// </summary>
        Task<SubstitutionReasonBreakdownResponse> GetReasonBreakdownAsync(
            CancellationToken ct = default);
    }
}
