using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Dietary Trend Deep Dive API — layer: B2B, base route: <c>api/DietaryDeepDive</c>.
    /// Provides detailed analysis of individual dietary tags (recipe counts, top ingredients,
    /// regional hotspots, growth trajectory) and cross-dietary co-occurrence data showing
    /// which tags most often appear together on the same recipe.
    /// All responses are wrapped in the Phase 2 <see cref="SafetyApiResponse{T}"/> envelope.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class DietaryDeepDiveController : B2BControllerBase
    {
        private readonly IDietaryDeepDiveService _service;
        private readonly ILogger<DietaryDeepDiveController> _logger;

        public DietaryDeepDiveController(
            IDietaryDeepDiveService service,
            ILogger<DietaryDeepDiveController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// <c>GET api/DietaryDeepDive/tag/{tag}</c> — Deep dive on a specific dietary tag.
        /// Returns the total recipe count for the tag, monthly trend data, the most-used
        /// ingredients in tagged recipes, regional usage hotspots, and a growth trajectory
        /// classification (rising / stable / declining).
        /// </summary>
        /// <param name="tag">The dietary tag to analyse (e.g., <c>"vegan"</c>, <c>"halal"</c>, <c>"keto"</c>).</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the tag detail payload.<br/>
        /// <c>400 Bad Request</c> — <paramref name="tag"/> is null or whitespace.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("tag/{tag}")]
        public async Task<ActionResult> GetTagDetail(
            string tag,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return ApiError("Dietary tag is required.");

            var result = await _service.GetTagDetailAsync(tag, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// <c>GET api/DietaryDeepDive/crossover</c> — Get cross-dietary co-occurrence analysis.
        /// Returns ranked tag pairs showing which dietary tags most frequently appear together
        /// on the same recipe, including overlap percentage and combined recipe count.
        /// Useful for identifying composite dietary niches (e.g., "vegan + gluten-free").
        /// </summary>
        /// <param name="take">Number of tag pairs to return. Clamped to 1–200; default <c>50</c>.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the crossover list.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("crossover")]
        public async Task<ActionResult> GetCrossover(
            [FromQuery] int take = 50,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 200);
            var result = await _service.GetCrossoverAsync(take, ct);
            return SafetyOk(result);
        }
    }
}
