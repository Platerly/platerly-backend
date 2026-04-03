using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Allergen Intelligence API — layer: B2B, base route: <c>api/AllergenAnalytics</c>.
    /// Provides platform-wide allergen prevalence statistics, per-allergen ingredient mappings,
    /// and safe-ingredient lookups for third-party dietary and meal-planning applications.
    /// All responses are wrapped in the Phase 2 <see cref="SafetyApiResponse{T}"/> envelope,
    /// which includes a confidence score, disclaimer, and tiered alert list.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
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
        /// <c>GET api/AllergenAnalytics/prevalence</c> — Get allergen prevalence across the user base.
        /// Returns an array of allergen entries showing how many registered users have each allergy,
        /// a severity distribution breakdown (Low / Medium / High), and trace-sensitivity flags.
        /// </summary>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the prevalence payload.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("prevalence")]
        public async Task<ActionResult> GetPrevalence(
            CancellationToken ct = default)
        {
            var result = await _analytics.GetPrevalenceAsync(ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// <c>GET api/AllergenAnalytics/ingredients/{allergenType}</c> — Get ingredients flagged for a specific allergen type.
        /// Returns a list of ingredients associated with the allergen, each annotated with severity level,
        /// may-contain flag, and cross-contact risk indicator.
        /// </summary>
        /// <param name="allergenType">The allergen type to look up (e.g., <c>"dairy"</c>, <c>"gluten"</c>, <c>"nuts"</c>).</param>
        /// <param name="take">Maximum number of results to return. Clamped to 1–200; default <c>50</c>.</param>
        /// <param name="severity">Optional severity filter. Accepted values: <c>Low</c>, <c>Medium</c>, <c>High</c>.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the ingredient list.<br/>
        /// <c>400 Bad Request</c> — <paramref name="allergenType"/> is null or whitespace.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
        /// <c>GET api/AllergenAnalytics/safe-ingredients</c> — Get ingredients confirmed safe for a given set of allergens.
        /// Returns ingredients that carry none of the specified allergens, with optional filtering
        /// to also exclude may-contain and cross-contact items.
        /// Useful for building compliant ingredient pickers in dietary apps and meal-planning services.
        /// </summary>
        /// <param name="allergens">Comma-separated allergen types (e.g., <c>"dairy,gluten,nuts"</c>). Required.</param>
        /// <param name="excludeMayContain">
        /// When <c>true</c> (default), ingredients labelled "may contain" the specified allergens are excluded.
        /// </param>
        /// <param name="excludeCrossContact">
        /// When <c>true</c>, ingredients with a cross-contact risk are also excluded. Default <c>false</c>.
        /// </param>
        /// <param name="take">Maximum number of results. Clamped to 1–500; default <c>100</c>.</param>
        /// <param name="category">Optional ingredient category slug to narrow results (e.g., <c>"grains"</c>).</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the safe ingredient list.<br/>
        /// <c>400 Bad Request</c> — <paramref name="allergens"/> is null or whitespace.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
