using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Recipe Engagement Analytics API.
    /// Platform-wide engagement statistics by recipe type, difficulty, and region.
    /// Requires API Key authentication.
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
        /// Get platform-wide engagement overview.
        /// Total recipes, views, likes, comments, reposts, saves, and averages per recipe.
        /// </summary>
        [HttpGet("overview")]
        public async Task<ActionResult> GetOverview(
            CancellationToken ct = default)
        {
            var result = await _service.GetOverviewAsync(ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// Get engagement breakdown by recipe type and difficulty.
        /// Shows which categories get the most interaction.
        /// </summary>
        [HttpGet("by-category")]
        public async Task<ActionResult> GetByCategory(
            CancellationToken ct = default)
        {
            var result = await _service.GetByCategoryAsync(ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// Get engagement breakdown by recipe region/cuisine.
        /// Shows which cuisines get the most interaction.
        /// </summary>
        /// <param name="take">Number of regions to return (default 50, max 200)</param>
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
