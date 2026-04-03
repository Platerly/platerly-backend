using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Recipe Engagement Analytics API — layer: B2B, base route: <c>api/EngagementAnalytics</c>.
    /// Exposes aggregated, anonymised platform-wide engagement metrics for recipes:
    /// totals, per-recipe averages, category breakdowns, and regional/cuisine breakdowns.
    /// All data is read-only; no user-level information is exposed.
    /// All responses are wrapped in the Phase 2 <see cref="SafetyApiResponse{T}"/> envelope.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class EngagementAnalyticsController : B2BControllerBase
    {
        private readonly IEngagementAnalyticsService _service;
        private readonly ILogger<EngagementAnalyticsController> _logger;

        public EngagementAnalyticsController(
            IEngagementAnalyticsService service,
            ILogger<EngagementAnalyticsController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// <c>GET api/EngagementAnalytics/overview</c> — Get platform-wide engagement overview.
        /// Returns lifetime totals for recipes, views, likes, comments, reposts, and saves,
        /// together with calculated per-recipe averages for each metric.
        /// </summary>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the engagement overview.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("overview")]
        public async Task<ActionResult> GetOverview(
            CancellationToken ct = default)
        {
            var result = await _service.GetOverviewAsync(ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// <c>GET api/EngagementAnalytics/by-category</c> — Get engagement breakdown by recipe type and difficulty.
        /// Returns a ranked list of recipe types and difficulty levels with their associated
        /// view, like, comment, and save counts, showing which categories drive the most interaction.
        /// </summary>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the category breakdown list.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("by-category")]
        public async Task<ActionResult> GetByCategory(
            CancellationToken ct = default)
        {
            var result = await _service.GetByCategoryAsync(ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// <c>GET api/EngagementAnalytics/by-region</c> — Get engagement breakdown by recipe region/cuisine.
        /// Returns a ranked list of regions/cuisines with their associated engagement metrics,
        /// showing which culinary traditions generate the highest user interaction.
        /// </summary>
        /// <param name="take">Number of regions to return. Clamped to 1–200; default <c>50</c>.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the regional breakdown list.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("by-region")]
        public async Task<ActionResult> GetByRegion(
            [FromQuery] int take = 50,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 200);
            var result = await _service.GetByRegionAsync(take, ct);
            return SafetyOk(result);
        }
    }
}
