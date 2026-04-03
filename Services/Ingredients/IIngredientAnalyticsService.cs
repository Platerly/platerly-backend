using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Ingredients
{
    /// <summary>Provides ingredient usage analytics, trending data, regional stats, pairings, and dietary trend queries.</summary>
    public interface IIngredientAnalyticsService
    {
        /// <summary>
        /// Get trending ingredients over a time period
        /// </summary>
        Task<TrendingIngredientsResponse> GetTrendingIngredientsAsync(
            TrendingIngredientsRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Get regional ingredient statistics
        /// </summary>
        Task<RegionalStatsResponse> GetRegionalStatsAsync(
            RegionalStatsRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Get ingredient pairings (what goes with what)
        /// </summary>
        Task<PairingsResponse> GetPairingsAsync(
            PairingsRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Get most popular ingredients globally or by region
        /// </summary>
        Task<PopularIngredientsResponse> GetPopularIngredientsAsync(
            PopularIngredientsRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Get search demand signals (what people are searching for)
        /// </summary>
        Task<SearchDemandResponse> GetSearchDemandAsync(
            SearchDemandRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Get dietary trend data
        /// </summary>
        Task<DietaryTrendsResponse> GetDietaryTrendsAsync(
            DietaryTrendsRequest request,
            CancellationToken ct = default);

        /// <summary>
        /// Get category analytics (usage by category)
        /// </summary>
        Task<CategoryAnalyticsResponse> GetCategoryAnalyticsAsync(
            CancellationToken ct = default);

        /// <summary>
        /// Get list of available regions
        /// </summary>
        Task<List<string>> GetAvailableRegionsAsync(CancellationToken ct = default);
    }
}
