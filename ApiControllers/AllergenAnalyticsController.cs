using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Allergen Intelligence API.
    /// Provides allergen prevalence data, ingredient-allergen mappings, and safe ingredient lookups.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class AllergenAnalyticsController : B2BControllerBase
    {
        private readonly IAllergenAnalyticsService _analytics;
        private readonly ILogger<AllergenAnalyticsController> _logger;

        public AllergenAnalyticsController(
            IAllergenAnalyticsService analytics,
            ILogger<AllergenAnalyticsController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _analytics = analytics;
            _logger = logger;
        }

        /// <summary>
        /// Get allergen prevalence across the user base.
        /// Shows how many users have each allergy, severity distribution, and trace sensitivity.
        /// </summary>
        [HttpGet("prevalence")]
        public async Task<ActionResult> GetPrevalence(
            CancellationToken ct = default)
        {
            var result = await _analytics.GetPrevalenceAsync(ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// Get ingredients flagged for a specific allergen type.
        /// Includes severity levels, may-contain flags, and cross-contact risks.
        /// </summary>
        /// <param name="allergenType">The allergen type to look up (e.g., "dairy", "gluten", "nuts")</param>
        /// <param name="take">Number of results (default 50, max 200)</param>
        /// <param name="severity">Optional severity filter (Low, Medium, High)</param>
        [HttpGet("ingredients/{allergenType}")]
        public async Task<ActionResult> GetIngredientsByAllergen(
            string allergenType,
            [FromQuery] int take = 50,
            [FromQuery] string? severity = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(allergenType))
                return ApiError("Allergen type is required.");

            take = Math.Clamp(take, 1, 200);

            var request = new AllergenIngredientsRequest
            {
                AllergenType = allergenType,
                Take = take,
                SeverityFilter = severity
            };

            var result = await _analytics.GetIngredientsByAllergenAsync(request, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// Get ingredients confirmed safe for a given set of allergens.
        /// Useful for dietary apps and meal planning services.
        /// </summary>
        /// <param name="allergens">Comma-separated allergen types (e.g., "dairy,gluten,nuts")</param>
        /// <param name="excludeMayContain">Exclude may-contain ingredients (default true)</param>
        /// <param name="excludeCrossContact">Exclude cross-contact risk ingredients (default false)</param>
        /// <param name="take">Number of results (default 100, max 500)</param>
        /// <param name="category">Optional category slug filter</param>
        [HttpGet("safe-ingredients")]
        public async Task<ActionResult> GetSafeIngredients(
            [FromQuery] string allergens,
            [FromQuery] bool excludeMayContain = true,
            [FromQuery] bool excludeCrossContact = false,
            [FromQuery] int take = 100,
            [FromQuery] string? category = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(allergens))
                return ApiError("At least one allergen type is required (comma-separated).");

            take = Math.Clamp(take, 1, 500);

            var request = new SafeIngredientsRequest
            {
                Allergens = allergens,
                ExcludeMayContain = excludeMayContain,
                ExcludeCrossContact = excludeCrossContact,
                Take = take,
                Category = category
            };

            var result = await _analytics.GetSafeIngredientsAsync(request, ct);
            return SafetyOk(result);
        }
    }
}
