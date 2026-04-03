using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Substitution Intelligence API — layer: B2B, base route: <c>api/SubstitutionAnalytics</c>.
    /// Provides analytics on how users substitute ingredients on the Recipzee platform:
    /// which substitutions are most popular, the specific substitutes available for a given
    /// ingredient, and a breakdown of the reasons behind substitution choices.
    /// Data includes confidence scores derived from community votes and usage frequency.
    /// All responses are wrapped in the Phase 2 <see cref="SafetyApiResponse{T}"/> envelope.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class SubstitutionAnalyticsController : B2BControllerBase
    {
        private readonly ISubstitutionAnalyticsService _analytics;
        private readonly ILogger<SubstitutionAnalyticsController> _logger;

        public SubstitutionAnalyticsController(
            ISubstitutionAnalyticsService analytics,
            ILogger<SubstitutionAnalyticsController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _analytics = analytics;
            _logger = logger;
        }

        /// <summary>
        /// <c>GET api/SubstitutionAnalytics/top</c> — Get top substitutions ranked by usage count.
        /// Returns the most-applied ingredient substitution pairs across the platform,
        /// with optional filtering by substitution reason, allergy, and region.
        /// </summary>
        /// <param name="reason">
        /// Optional reason filter. Accepted values: <c>Allergy</c>, <c>DietaryPreference</c>,
        /// <c>Availability</c>, <c>Cost</c>, <c>Taste</c>, <c>Health</c>, <c>Religious</c>, <c>Other</c>.
        /// </param>
        /// <param name="allergyId">Optional GUID of a specific allergy to scope results to allergy-driven substitutions.</param>
        /// <param name="region">Optional region name to scope results geographically.</param>
        /// <param name="take">Number of results to return. Clamped to 1–100; default <c>20</c>.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the top substitutions list.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("top")]
        public async Task<ActionResult> GetTop(
            [FromQuery] string? reason = null,
            [FromQuery] Guid? allergyId = null,
            [FromQuery] string? region = null,
            [FromQuery] int take = 20,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 100);

            var request = new TopSubstitutionsRequest
            {
                Reason = reason,
                AllergyId = allergyId,
                Region = region,
                Take = take
            };

            var result = await _analytics.GetTopSubstitutionsAsync(request, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// <c>GET api/SubstitutionAnalytics/{ingredientId}</c> — Get all recorded substitutes for a specific ingredient.
        /// Returns every substitute ingredient that users have applied in place of the given ingredient,
        /// each annotated with a confidence score (0–1), vote count, and quantity conversion ratio.
        /// Optionally filter to a specific substitution reason.
        /// </summary>
        /// <param name="ingredientId">GUID of the global ingredient to look up substitutes for.</param>
        /// <param name="reason">Optional reason filter (same accepted values as <c>GetTop</c>).</param>
        /// <param name="take">Number of substitutes to return. Clamped to 1–200; default <c>50</c>.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the substitute list.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("{ingredientId}")]
        public async Task<ActionResult> GetForIngredient(
            Guid ingredientId,
            [FromQuery] string? reason = null,
            [FromQuery] int take = 50,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 200);

            var request = new IngredientSubstitutionsRequest
            {
                IngredientId = ingredientId,
                Reason = reason,
                Take = take
            };

            var result = await _analytics.GetSubstitutionsForIngredientAsync(request, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// <c>GET api/SubstitutionAnalytics/reasons</c> — Get a breakdown of substitutions by reason.
        /// Returns each reason category with its total substitution count, percentage share of all
        /// substitutions, and average confidence score — giving a high-level view of why users
        /// substitute ingredients on the platform.
        /// </summary>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the reason breakdown list.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("reasons")]
        public async Task<ActionResult> GetReasonBreakdown(
            CancellationToken ct = default)
        {
            var result = await _analytics.GetReasonBreakdownAsync(ct);
            return SafetyOk(result);
        }
    }
}
