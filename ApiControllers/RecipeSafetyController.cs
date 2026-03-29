using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Models;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Recipe Safety Audit API.
    /// Audit recipes for allergen risks, prohibited ingredients, and cross-contact warnings.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class RecipeSafetyController : B2BControllerBase
    {
        private readonly IRecipeSafetyService _safety;
        private readonly ISafetyAuditService _audit;
        private readonly ILogger<RecipeSafetyController> _logger;

        public RecipeSafetyController(
            IRecipeSafetyService safety,
            ISafetyAuditService audit,
            ILogger<RecipeSafetyController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _safety = safety;
            _audit  = audit;
            _logger = logger;
        }

        /// <summary>
        /// Audit a single recipe for safety concerns.
        /// Submit an ingredient list and get allergen risks, prohibited flags, cross-contact warnings,
        /// and (when AllergenProfile is provided) top-2 safe substitutes per flagged ingredient.
        /// </summary>
        [HttpPost("audit")]
        public async Task<ActionResult> AuditRecipe(
            [FromBody] RecipeSafetyAuditRequest request,
            CancellationToken ct = default)
        {
            if (request.Ingredients == null || request.Ingredients.Count == 0)
                return ApiError("At least one ingredient is required.");

            if (request.Ingredients.Count > 200)
                return ApiError("Maximum 200 ingredients per audit.");

            if (request.AllergenProfile != null && request.AllergenProfile.Count > 30)
                return ApiError("AllergenProfile may contain at most 30 entries.");

            var result = await _safety.AuditRecipeAsync(request, ct);

            // ── Phase 2 safety alerts ────────────────────────────────────────
            var alerts = new List<SafetyAlertDto>();

            if (result.OverallRiskLevel == "High")
                alerts.Add(new SafetyAlertDto { Severity = "FATAL", Code = "HIGH_RISK_RECIPE",
                    Message = "One or more ingredients pose a high allergen or safety risk. Do not serve to affected individuals." });

            if (result.OverallRiskLevel == "Medium")
                alerts.Add(new SafetyAlertDto { Severity = "WARNING", Code = "MEDIUM_RISK_RECIPE",
                    Message = "Multiple allergen or cross-contact flags detected. Review flagged ingredients carefully." });

            if (result.ProhibitedFlags.Count > 0)
                alerts.Add(new SafetyAlertDto { Severity = "FATAL", Code = "PROHIBITED_INGREDIENTS_FOUND",
                    Message = $"{result.ProhibitedFlags.Count} prohibited ingredient(s) detected. Recipe must be reformulated." });

            if (result.CrossContactFlags.Count > 0)
                alerts.Add(new SafetyAlertDto { Severity = "WARNING", Code = "CROSS_CONTACT_RISK",
                    Message = "Some ingredients carry a cross-contact risk. Ensure separation protocols are followed during preparation." });

            if (request.AllergenProfile != null && request.AllergenProfile.Count > 0)
                alerts.Add(new SafetyAlertDto { Severity = "INFO", Code = "ALLERGEN_PROFILE_FILTER_ACTIVE",
                    Message = "Results are filtered to your submitted allergen profile. Other allergens in this recipe are not shown." });

            alerts.Add(new SafetyAlertDto { Severity = "INFO", Code = "CLINICAL_DISCLAIMER",
                Message = "This output is informational only and does not constitute medical or dietary advice." });

            // ── Fire-and-forget audit log ────────────────────────────────────
            _ = _audit.LogAsync(new SafetyAuditLog
            {
                ApiKeyId             = GetApiKeyId(),
                Endpoint             = "RecipeSafety/audit",
                IngredientContext    = string.Join(",", request.Ingredients.Take(20)),
                AllergyContext       = request.AllergenProfile != null
                    ? string.Join(",", request.AllergenProfile) : null,
                AlertCount           = alerts.Count,
                HighestAlertSeverity = alerts.Any(a => a.Severity == "FATAL") ? "FATAL"
                    : alerts.Any(a => a.Severity == "WARNING") ? "WARNING" : "INFO",
                TimestampUtc         = DateTime.UtcNow
            });

            var response = new SafetyApiResponse<RecipeSafetyAuditResponse>
            {
                Data   = result,
                Safety = _safetyMeta.BuildBaseline()
            };
            response.Safety.Alerts = alerts;

            return Ok(response);
        }

        /// <summary>
        /// Batch audit multiple recipes at once (max 50).
        /// AllergenProfile can be supplied per-recipe in the batch.
        /// Returns per-recipe breakdown and summary.
        /// </summary>
        [HttpPost("batch")]
        public async Task<ActionResult> BatchAudit(
            [FromBody] BatchAuditRequest request,
            CancellationToken ct = default)
        {
            if (request.Recipes == null || request.Recipes.Count == 0)
                return ApiError("At least one recipe is required.");

            if (request.Recipes.Count > 50)
                return ApiError("Maximum 50 recipes per batch audit.");

            foreach (var recipe in request.Recipes)
            {
                if (recipe.AllergenProfile != null && recipe.AllergenProfile.Count > 30)
                    return ApiError("AllergenProfile may contain at most 30 entries per recipe.");
            }

            var result = await _safety.BatchAuditAsync(request, ct);

            var alerts = new List<SafetyAlertDto>
            {
                new() { Severity = "INFO", Code = "BATCH_AUDIT",
                    Message = $"{result.TotalRecipes} recipes audited, {result.RecipesWithFlags} with flags." },
                new() { Severity = "INFO", Code = "CLINICAL_DISCLAIMER",
                    Message = "Results are informational only and do not constitute medical or dietary advice." }
            };

            _ = _audit.LogAsync(new SafetyAuditLog
            {
                ApiKeyId             = GetApiKeyId(),
                Endpoint             = "RecipeSafety/batch",
                IngredientContext    = $"{request.Recipes.Count} recipes",
                AlertCount           = alerts.Count,
                HighestAlertSeverity = result.RecipesWithFlags > 0 ? "WARNING" : "INFO",
                TimestampUtc         = DateTime.UtcNow
            });

            var response = new SafetyApiResponse<BatchAuditResponse>
            {
                Data   = result,
                Safety = _safetyMeta.BuildBaseline()
            };
            response.Safety.Alerts = alerts;

            return Ok(response);
        }
    }
}
