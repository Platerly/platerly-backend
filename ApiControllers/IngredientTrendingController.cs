using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Ingredient Trending API.
    /// Exposes time-series ingredient usage trends aggregated from IngredientTrendDaily.
    /// Filterable by region, category, and trend direction.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/b2b/ingredients")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class IngredientTrendingController : B2BControllerBase
    {
        private readonly IIngredientTrendingService _trending;
        private readonly ILogger<IngredientTrendingController> _logger;

        private static readonly HashSet<string> ValidSortBy =
            new(StringComparer.OrdinalIgnoreCase) { "usage", "rank", "growth" };

        private static readonly HashSet<string> ValidTrend =
            new(StringComparer.OrdinalIgnoreCase) { "rising", "declining", "stable" };

        public IngredientTrendingController(
            IIngredientTrendingService trending,
            ISafetyMetaService safetyMeta,
            ILogger<IngredientTrendingController> logger)
            : base(safetyMeta)
        {
            _trending = trending;
            _logger = logger;
        }

        /// <summary>
        /// Get trending ingredients for a given period.
        /// Aggregates IngredientTrendDaily records — pre-computed daily rollups from
        /// recipe creation activity. Includes nickel classification on each ingredient.
        /// </summary>
        /// <param name="region">Optional region filter (e.g., "Mediterranean", "Asian"). Omit for global data.</param>
        /// <param name="category">Optional ingredient category filter (e.g., "Vegetables", "Dairy").</param>
        /// <param name="trend">Optional direction filter: "rising", "declining", or "stable".</param>
        /// <param name="days">Look-back window in days (default 30, max 90).</param>
        /// <param name="take">Number of ingredients to return (default 50, max 200).</param>
        /// <param name="sortBy">Sort order: "usage" (default), "rank", or "growth".</param>
        [HttpGet("trending")]
        public async Task<ActionResult> GetTrending(
            [FromQuery] string? region = null,
            [FromQuery] string? category = null,
            [FromQuery] string? trend = null,
            [FromQuery] int days = 30,
            [FromQuery] int take = 50,
            [FromQuery] string sortBy = "usage",
            CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(trend) && !ValidTrend.Contains(trend))
                return ApiError($"Invalid trend value '{trend}'. Use: rising, declining, stable.");

            if (!string.IsNullOrEmpty(sortBy) && !ValidSortBy.Contains(sortBy))
                return ApiError($"Invalid sortBy value '{sortBy}'. Use: usage, rank, growth.");

            days = Math.Clamp(days, 1, 90);
            take = Math.Clamp(take, 1, 200);

            var request = new IngredientTrendingRequest
            {
                Region = region,
                Category = category,
                Trend = trend,
                Days = days,
                Take = take,
                SortBy = sortBy
            };

            var result = await _trending.GetTrendingAsync(request, ct);
            return SafetyOk(result);
        }
    }
}
