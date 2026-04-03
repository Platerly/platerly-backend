using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Ingredients;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Analytics API exposing ingredient usage trends, regional popularity,
    /// ingredient pairings, dietary shifts, and search demand signals aggregated
    /// from Recipzee platform data.
    /// Base route: <c>api/analytics</c>.
    /// Auth: API Key (<c>ApiKeyAuthenticationOptions.DefaultScheme</c>). Keys are managed
    /// through the <c>ApiKeyController</c> and must be passed in the
    /// <c>X-Api-Key</c> request header.
    /// All <c>take</c> / <c>days</c> parameters are clamped server-side to prevent abuse.
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
        /// Get the ingredients that are gaining the most popularity over the specified time window.
        /// Results can be scoped to a region and/or ingredient category slug.
        /// </summary>
        /// <param name="region">Optional region name to scope results (e.g., "canada", "uk").</param>
        /// <param name="days">Lookback window in days, clamped 1–90 (default 7).</param>
        /// <param name="take">Maximum results to return, clamped 1–100 (default 20).</param>
        /// <param name="category">Optional ingredient category slug filter.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — <see cref="TrendingIngredientsResponse"/>.</returns>
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
        /// Get ingredient popularity and usage statistics for a specific region.
        /// </summary>
        /// <param name="region">Region name (required, case-insensitive).</param>
        /// <param name="take">Maximum results, clamped 1–200 (default 50).</param>
        /// <param name="category">Optional ingredient category slug filter.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="RegionalStatsResponse"/>.<br/>
        /// <c>400</c> — region not provided.
        /// </returns>
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
        /// Retrieve ingredient pairing data — which ingredients most commonly appear together
        /// in recipes. Valuable for product development and recipe recommendation engines.
        /// </summary>
        /// <param name="ingredientId">Optional — restrict pairings to a specific ingredient GUID.</param>
        /// <param name="take">Maximum results, clamped 1–100 (default 20).</param>
        /// <param name="minAffinityScore">Optional minimum affinity score (0–100) to filter weak pairings.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — <see cref="PairingsResponse"/>.</returns>
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
        /// Get the most-used ingredients globally or filtered by region, category, and/or dietary flag.
        /// </summary>
        /// <param name="region">Optional region name filter.</param>
        /// <param name="category">Optional ingredient category slug filter.</param>
        /// <param name="take">Maximum results, clamped 1–200 (default 50).</param>
        /// <param name="dietary">Optional dietary flag filter (e.g., "vegan", "halal", "gluten-free").</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — <see cref="PopularIngredientsResponse"/>.</returns>
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
        /// Get search demand signals showing which ingredient or recipe terms users are searching
        /// for on the platform. Set <paramref name="onlyUnfound"/> to surface queries that returned
        /// zero results — a strong indicator of unmet demand.
        /// </summary>
        /// <param name="days">Lookback window in days, clamped 1–365 (default 30).</param>
        /// <param name="take">Maximum results, clamped 1–200 (default 50).</param>
        /// <param name="region">Optional region filter.</param>
        /// <param name="onlyUnfound">When true, restricts to searches that produced no results.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — <see cref="SearchDemandResponse"/>.</returns>
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
        /// Retrieve trend data for dietary preferences (vegan, halal, keto, gluten-free, etc.)
        /// over the given time window, showing growth trajectories per preference.
        /// </summary>
        /// <param name="days">Lookback window in days, clamped 1–365 (default 30).</param>
        /// <param name="region">Optional region filter.</param>
        /// <param name="tag">Optional specific dietary tag to drill into (e.g., "halal").</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — <see cref="DietaryTrendsResponse"/>.</returns>
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
        /// Get a usage breakdown by ingredient category showing counts and relative share.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — <see cref="CategoryAnalyticsResponse"/>.</returns>
        [HttpGet("categories")]
        public async Task<ActionResult<CategoryAnalyticsResponse>> GetCategories(
            CancellationToken ct = default)
        {
            var result = await _analytics.GetCategoryAnalyticsAsync(ct);
            return Ok(result);
        }

        /// <summary>
        /// List all region identifiers present in the analytics dataset.
        /// Use these values as the <c>region</c> parameter in other analytics endpoints.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — array of region name strings.</returns>
        [HttpGet("regions")]
        public async Task<ActionResult<List<string>>> GetRegions(CancellationToken ct = default)
        {
            var regions = await _analytics.GetAvailableRegionsAsync(ct);
            return Ok(regions);
        }
    }
}
