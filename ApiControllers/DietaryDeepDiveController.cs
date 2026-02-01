using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Dietary Trend Deep Dive API.
    /// Detailed analysis of dietary tags and cross-dietary patterns.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class DietaryDeepDiveController : ControllerBase
    {
        private readonly IDietaryDeepDiveService _service;
        private readonly ILogger<DietaryDeepDiveController> _logger;

        public DietaryDeepDiveController(
            IDietaryDeepDiveService service,
            ILogger<DietaryDeepDiveController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// Deep dive on a specific dietary tag.
        /// Recipe count trends, top ingredients, regional hotspots, and growth trajectory.
        /// </summary>
        /// <param name="tag">The dietary tag to analyze (e.g., "vegan", "halal", "keto")</param>
        [HttpGet("tag/{tag}")]
        public async Task<ActionResult<DietaryTagDetailResponse>> GetTagDetail(
            string tag,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return BadRequest("Dietary tag is required");
            }

            var result = await _service.GetTagDetailAsync(tag, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get cross-dietary analysis.
        /// Which dietary tags co-occur on recipes and overlap percentages.
        /// </summary>
        /// <param name="take">Number of tag pairs to return (default 50, max 200)</param>
        [HttpGet("crossover")]
        public async Task<ActionResult<DietaryCrossoverResponse>> GetCrossover(
            [FromQuery] int take = 50,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 200);
            var result = await _service.GetCrossoverAsync(take, ct);
            return Ok(result);
        }
    }
}
