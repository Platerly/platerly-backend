using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Substitution Intelligence API.
    /// Provides analytics on ingredient substitution patterns, usage, and effectiveness.
    /// Requires API Key authentication.
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
        /// Get top substitutions by usage count.
        /// Filterable by reason (Allergy, DietaryPreference, Religious, etc.) and allergy ID.
        /// </summary>
        /// <param name="reason">Optional substitution reason filter (Allergy, DietaryPreference, Availability, Cost, Taste, Health, Religious, Other)</param>
        /// <param name="allergyId">Optional allergy ID filter</param>
        /// <param name="region">Optional region filter</param>
        /// <param name="take">Number of results (default 20, max 100)</param>
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
        /// Get all substitutes for a specific ingredient.
        /// Returns substitutions with confidence scores, votes, and conversion ratios.
        /// </summary>
        /// <param name="ingredientId">The global ingredient ID to find substitutes for</param>
        /// <param name="reason">Optional reason filter</param>
        /// <param name="take">Number of results (default 50, max 200)</param>
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
        /// Get breakdown of substitutions by reason.
        /// Shows counts, percentages, and average confidence scores per reason category.
        /// </summary>
        [HttpGet("reasons")]
        public async Task<ActionResult> GetReasonBreakdown(
            CancellationToken ct = default)
        {
            var result = await _analytics.GetReasonBreakdownAsync(ct);
            return SafetyOk(result);
        }
    }
}
