using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Ingredient Trending API — layer: B2B, base route: <c>api/b2b/ingredients</c>.
    /// Exposes pre-computed daily ingredient usage rollups from the <c>IngredientTrendDaily</c>
    /// table, which is populated from recipe creation activity. Each entry includes total usage
    /// count, percentile rank, growth rate versus the prior period, trend direction classification,
    /// and nickel classification from the Rebelytics v9.1.1 dataset.
    /// All responses are wrapped in the Phase 2 <see cref="SafetyApiResponse{T}"/> envelope.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
    /// </summary>
    [ApiController]
    [Route("api/b2b/ingredients")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class IngredientTrendingController : B2BControllerBase
    {
        private readonly IIngredientTrendingService _trending;
        private readonly ILogger<IngredientTrendingController> _logger;

        // Accepted sort-by values; validated before reaching the service layer.
        private static readonly HashSet<string> ValidSortBy =
            new(StringComparer.OrdinalIgnoreCase) { "usage", "rank", "growth" };

        // Accepted trend-direction filter values.
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
        /// <c>GET api/b2b/ingredients/trending</c> — Get trending ingredients for a rolling time window.
        /// Aggregates <c>IngredientTrendDaily</c> rows within the look-back window specified by
        /// <paramref name="days"/>. The <c>growth</c> field on each result is the percentage
        /// change compared to the equivalent prior period (e.g., the preceding 30 days when
        /// <paramref name="days"/> is 30). Each ingredient also includes its nickel classification.
        /// </summary>
        /// <param name="region">Optional region filter (e.g., <c>"Mediterranean"</c>, <c>"Asian"</c>). Omit for global data.</param>
        /// <param name="category">Optional ingredient category filter (e.g., <c>"Vegetables"</c>, <c>"Dairy"</c>).</param>
        /// <param name="trend">
        /// Optional direction filter. Accepted values: <c>"rising"</c>, <c>"declining"</c>, <c>"stable"</c>.
        /// </param>
        /// <param name="days">Look-back window in days. Clamped to 1–90; default <c>30</c>.</param>
        /// <param name="take">Number of ingredients to return. Clamped to 1–200; default <c>50</c>.</param>
        /// <param name="sortBy">
        /// Sort order: <c>"usage"</c> (total usage count, default), <c>"rank"</c> (percentile rank),
        /// or <c>"growth"</c> (period-over-period growth rate).
        /// </param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the trending ingredients list.<br/>
        /// <c>400 Bad Request</c> — invalid <paramref name="trend"/> or <paramref name="sortBy"/> value.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
