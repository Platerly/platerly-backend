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
    /// B2B Recipe Safety Audit API. Audits recipes for allergen risks, prohibited ingredients,
    /// and cross-contact warnings.
    ///
    /// Base route: /api/RecipeSafety
    /// Auth requirement: API Key (<c>ApiKeyAuthenticationOptions.DefaultScheme</c>).
    ///
    /// Phase 2 safety layer (applied to every response via <see cref="SafetyApiResponse{T}"/>):
    ///   • Wraps all success responses in an envelope that includes a <c>Safety</c> block
    ///     containing a disclaimer, confidence score, data freshness info, and contextual alerts.
    ///   • Alert severity levels: FATAL, WARNING, INFO.
    ///   • Alerts are generated for: HIGH/MEDIUM overall risk levels, prohibited ingredient
    ///     flags, cross-contact flags, and an active allergen profile filter.
    ///   • A CLINICAL_DISCLAIMER INFO alert is always included to communicate that the output
    ///     is informational only and does not constitute medical or dietary advice.
    ///   • All audit calls are logged fire-and-forget via <see cref="ISafetyAuditService"/>.
    ///
    /// Endpoints:
    ///   POST /api/RecipeSafety/audit  — single recipe, up to 200 ingredients
    ///   POST /api/RecipeSafety/batch  — up to 50 recipes per call
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
        /// Audits a single recipe for safety concerns.
        ///
        /// Submit an ingredient list (up to 200) and receive:
        ///   • Per-ingredient allergen flags with severity (High/Medium/Low), detection source,
        ///     may-contain and cross-contact indicators.
        ///   • Overall risk level (High/Medium/Low/None).
        ///   • Prohibited ingredient flags (e.g. ingredients blocked for a specific dietary context).
        ///   • Cross-contact risk flags.
        ///   • When <c>AllergenProfile</c> is supplied, the top-2 safe substitutes per flagged
        ///     ingredient are included in the response.
        ///
        /// Phase 2 safety alerts appended to the response:
        ///   FATAL   — HIGH_RISK_RECIPE, PROHIBITED_INGREDIENTS_FOUND
        ///   WARNING — MEDIUM_RISK_RECIPE, CROSS_CONTACT_RISK
        ///   INFO    — ALLERGEN_PROFILE_FILTER_ACTIVE (when profile supplied), CLINICAL_DISCLAIMER
        /// </summary>
        /// <param name="request">
        /// <c>Ingredients</c>: 1–200 ingredient name strings.
        /// <c>AllergenProfile</c>: optional list of up to 30 allergen types to filter results by.
        /// </param>
        /// <returns>
        /// 200 with <see cref="SafetyApiResponse{RecipeSafetyAuditResponse}"/>;
        /// 400 on validation failures.
        /// Side effects: fire-and-forget audit log written via <see cref="ISafetyAuditService"/>.
        /// </returns>
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
        /// Batch-audits up to 50 recipes in a single request.
        ///
        /// <c>AllergenProfile</c> can be supplied per-recipe in the batch to customise filtering
        /// per recipe. Returns a per-recipe breakdown and a summary of total recipes vs recipes
        /// with flags.
        ///
        /// Phase 2 safety alerts included: BATCH_AUDIT (summary counts), CLINICAL_DISCLAIMER.
        /// </summary>
        /// <param name="request">
        /// <c>Recipes</c>: list of 1–50 recipe audit requests, each with its own ingredient list
        /// and optional allergen profile (max 30 allergens per recipe).
        /// </param>
        /// <returns>
        /// 200 with <see cref="SafetyApiResponse{BatchAuditResponse}"/>;
        /// 400 on validation failures.
        /// Side effects: fire-and-forget audit log written via <see cref="ISafetyAuditService"/>.
        /// </returns>
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
