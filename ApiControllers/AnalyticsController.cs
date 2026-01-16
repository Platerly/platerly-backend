using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Ingredients;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Analytics API for ingredient trends, regional data, and market insights.
    /// Requires API Key authentication. Get your API key from the developer dashboard.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class AnalyticsController : ControllerBase
    {
        private readonly IIngredientAnalyticsService _analytics;
        private readonly ILogger<AnalyticsController> _logger;

        public AnalyticsController(
            IIngredientAnalyticsService analytics,
            ILogger<AnalyticsController> logger)
        {
            _analytics = analytics;
            _logger = logger;
        }

        /// <summary>
        /// Get trending ingredients over a time period.
        /// Shows what ingredients are gaining popularity.
        /// </summary>
        /// <param name="region">Optional region filter</param>
        /// <param name="days">Number of days to analyze (default 7)</param>
        /// <param name="take">Number of results (default 20, max 100)</param>
        /// <param name="category">Optional category slug filter</param>
        [HttpGet("trending")]
        public async Task<ActionResult<TrendingIngredientsResponse>> GetTrending(
            [FromQuery] string? region = null,
            [FromQuery] int days = 7,
            [FromQuery] int take = 20,
            [FromQuery] string? category = null,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 100);
            days = Math.Clamp(days, 1, 90);

            var request = new TrendingIngredientsRequest
            {
                Region = region,
                Days = days,
                Take = take,
                Category = category
            };

            var result = await _analytics.GetTrendingIngredientsAsync(request, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get regional ingredient statistics.
        /// Shows ingredient popularity and usage patterns by region.
        /// </summary>
        /// <param name="region">Required region name</param>
        /// <param name="take">Number of results (default 50, max 200)</param>
        /// <param name="category">Optional category slug filter</param>
        [HttpGet("regional/{region}")]
        public async Task<ActionResult<RegionalStatsResponse>> GetRegionalStats(
            string region,
            [FromQuery] int take = 50,
            [FromQuery] string? category = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(region))
            {
                return BadRequest("Region is required");
            }

            take = Math.Clamp(take, 1, 200);

            var request = new RegionalStatsRequest
            {
                Region = region,
                Take = take,
                Category = category
            };

            var result = await _analytics.GetRegionalStatsAsync(request, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get ingredient pairings (what ingredients commonly appear together).
        /// Valuable for product development and recipe suggestions.
        /// </summary>
        /// <param name="ingredientId">Optional - get pairings for specific ingredient</param>
        /// <param name="take">Number of results (default 20, max 100)</param>
        /// <param name="minAffinityScore">Optional minimum affinity score (0-100)</param>
        [HttpGet("pairings")]
        public async Task<ActionResult<PairingsResponse>> GetPairings(
            [FromQuery] Guid? ingredientId = null,
            [FromQuery] int take = 20,
            [FromQuery] decimal? minAffinityScore = null,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 100);

            var request = new PairingsRequest
            {
                IngredientId = ingredientId,
                Take = take,
                MinAffinityScore = minAffinityScore
            };

            var result = await _analytics.GetPairingsAsync(request, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get most popular ingredients globally or filtered by region/category.
        /// </summary>
        /// <param name="region">Optional region filter</param>
        /// <param name="category">Optional category slug filter</param>
        /// <param name="take">Number of results (default 50, max 200)</param>
        /// <param name="dietary">Optional dietary filter (e.g., "vegan", "halal")</param>
        [HttpGet("popular")]
        public async Task<ActionResult<PopularIngredientsResponse>> GetPopular(
            [FromQuery] string? region = null,
            [FromQuery] string? category = null,
            [FromQuery] int take = 50,
            [FromQuery] string? dietary = null,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 200);

            var request = new PopularIngredientsRequest
            {
                Region = region,
                Category = category,
                Take = take,
                DietaryFilter = dietary
            };

            var result = await _analytics.GetPopularIngredientsAsync(request, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get search demand signals - what people are searching for.
        /// Valuable for identifying unmet ingredient demand.
        /// </summary>
        /// <param name="days">Number of days to analyze (default 30)</param>
        /// <param name="take">Number of results (default 50, max 200)</param>
        /// <param name="region">Optional region filter</param>
        /// <param name="onlyUnfound">Only show searches with no results</param>
        [HttpGet("search-demand")]
        public async Task<ActionResult<SearchDemandResponse>> GetSearchDemand(
            [FromQuery] int days = 30,
            [FromQuery] int take = 50,
            [FromQuery] string? region = null,
            [FromQuery] bool? onlyUnfound = null,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 200);
            days = Math.Clamp(days, 1, 365);

            var request = new SearchDemandRequest
            {
                Days = days,
                Take = take,
                Region = region,
                OnlyUnfound = onlyUnfound
            };

            var result = await _analytics.GetSearchDemandAsync(request, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get dietary trend data (vegan, halal, keto, etc.)
        /// Shows which dietary preferences are growing.
        /// </summary>
        /// <param name="days">Number of days to analyze (default 30)</param>
        /// <param name="region">Optional region filter</param>
        /// <param name="tag">Optional specific dietary tag filter</param>
        [HttpGet("dietary-trends")]
        public async Task<ActionResult<DietaryTrendsResponse>> GetDietaryTrends(
            [FromQuery] int days = 30,
            [FromQuery] string? region = null,
            [FromQuery] string? tag = null,
            CancellationToken ct = default)
        {
            days = Math.Clamp(days, 1, 365);

            var request = new DietaryTrendsRequest
            {
                Days = days,
                Region = region,
                DietaryTag = tag
            };

            var result = await _analytics.GetDietaryTrendsAsync(request, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get category analytics - usage breakdown by ingredient category.
        /// </summary>
        [HttpGet("categories")]
        public async Task<ActionResult<CategoryAnalyticsResponse>> GetCategories(
            CancellationToken ct = default)
        {
            var result = await _analytics.GetCategoryAnalyticsAsync(ct);
            return Ok(result);
        }

        /// <summary>
        /// Get list of available regions in the dataset.
        /// </summary>
        [HttpGet("regions")]
        public async Task<ActionResult<List<string>>> GetRegions(CancellationToken ct = default)
        {
            var regions = await _analytics.GetAvailableRegionsAsync(ct);
            return Ok(regions);
        }
    }
}
