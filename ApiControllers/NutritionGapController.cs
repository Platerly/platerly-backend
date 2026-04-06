using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Nutrition;
using Platerly.Api.Models;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Nutritional Gap Analysis API. Identifies nutrient deficiency risks arising from
    /// a multi-allergen avoidance profile and returns allergen-safe compensating food recommendations.
    ///
    /// Base route: /api/b2b/nutrition
    /// Auth requirement: API Key (<c>ApiKeyAuthenticationOptions.DefaultScheme</c>).
    ///
    /// How it works:
    ///   • The service maps each submitted allergen to the food categories it eliminates from
    ///     the diet (e.g. "Milk" removes dairy → calcium, vitamin D, B12 gap risk).
    ///   • Each identified nutrient gap includes: risk level, triggering allergens, clinical
    ///     rationale, RDA/DRI reference, and a list of allergen-safe compensating foods.
    ///   • Compensating foods are validated against the GlobalIngredients DB; USDA macros
    ///     (calories, protein, carbs, fat, fiber) are included where available in NutritionCache.
    ///     Micronutrient quantities are not calculated — gap analysis is category-based.
    ///   • Overall dietary risk classification: NONE / LOW / MODERATE / HIGH / CRITICAL.
    ///
    /// Nickel co-filtering:
    ///   When <c>nickelSensitive = true</c>, every compensating food is additionally screened
    ///   against the Rebelytics v9.1.1 nickel database — only LOW-nickel foods are returned.
    ///   Foods with no nickel data are included but flagged with <c>nickelDataMissing = true</c>.
    ///
    /// RDI reference: Institute of Medicine / National Academies Dietary Reference Intakes (DRI).
    ///
    /// POST /api/b2b/nutrition/gap-analysis — requires ≥1 allergen (up to 30).
    ///
    /// Applies the full Phase 2 safety layer. All requests audit-logged fire-and-forget.
    /// </summary>
    [ApiController]
    [Route("api/b2b/nutrition")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class NutritionGapController : B2BControllerBase
    {
        private readonly INutritionGapService _gapService;
        private readonly ISafetyAuditService _audit;
        private readonly ILogger<NutritionGapController> _logger;
        private readonly IConfiguration _config;

        public NutritionGapController(
            INutritionGapService gapService,
            ISafetyAuditService audit,
            ISafetyMetaService safetyMeta,
            IConfiguration config,
            ILogger<NutritionGapController> logger)
            : base(safetyMeta)
        {
            _gapService = gapService;
            _audit      = audit;
            _config     = config;
            _logger     = logger;
        }

        /// <summary>
        /// Analyses nutritional gaps arising from a multi-allergen avoidance profile.
        ///
        /// Returns:
        ///   • <c>nutrientGaps</c> — per-nutrient entries with risk level (NONE/LOW/MODERATE/HIGH/CRITICAL),
        ///     list of triggering allergens, clinical rationale, RDA/DRI reference value, and
        ///     <c>compensatingFoods</c> (allergen-safe foods with USDA macros where available).
        ///   • <c>overallRisk</c> — aggregate risk classification for the allergen combination.
        ///   • <c>priorityNutrients</c> — ordered list of highest-severity gaps.
        ///   • <c>unmodelledAllergens</c> — allergens not in the knowledge base (gaps may be underestimated).
        ///   • <c>totalGapsIdentified</c>, <c>nickelExcludedCount</c> per gap (when nickelSensitive).
        ///
        /// Phase 2 safety alerts appended to the response:
        ///   WARNING — UNMODELLED_ALLERGENS, CRITICAL_NUTRITIONAL_RISK, HIGH_NUTRITIONAL_RISK
        ///   INFO    — NO_GAPS_IDENTIFIED, NICKEL_FILTER_ACTIVE, FOODS_NOT_IN_DATABASE,
        ///             MACROS_DATA_SCOPE, CLINICAL_DISCLAIMER
        /// </summary>
        /// <param name="request">
        /// <c>AllergenProfile</c>: 1–30 allergen type strings.
        /// <c>NickelSensitive</c>: when true, restricts compensating foods to Rebelytics LOW-nickel foods.
        /// <c>MaxFoodsPerGap</c>: 1–20 compensating food suggestions per gap (default varies by service).
        /// <c>FocusNutrients</c>: optional list of up to 20 nutrient names to restrict output to.
        /// </param>
        /// <returns>
        /// 200 with <see cref="SafetyApiResponse{NutritionGapResult}"/>;
        /// 400 on validation failures.
        /// Side effects: fire-and-forget audit log via <see cref="ISafetyAuditService"/>.
        /// </returns>
        [HttpPost("gap-analysis")]
        public async Task<ActionResult> AnalyseGaps(
            [FromBody] NutritionGapRequest request,
            CancellationToken ct = default)
        {
            if (!HasScope("clinical"))
                return ApiError(
                    "The 'clinical' scope is required for this endpoint. " +
                    "Upgrade to the Clinical Research tier at recipzee.com/api",
                    403);

            var sw = Stopwatch.StartNew();

            // ── Validate ────────────────────────────────────────────────────────

            if (request.AllergenProfile == null || request.AllergenProfile.Count == 0)
                return ApiError("At least one allergen is required in allergenProfile.");

            if (request.AllergenProfile.Count > 30)
                return ApiError("allergenProfile must not exceed 30 allergens.");

            if (request.MaxFoodsPerGap < 1 || request.MaxFoodsPerGap > 20)
                return ApiError("maxFoodsPerGap must be between 1 and 20.");

            if (request.FocusNutrients?.Count > 20)
                return ApiError("focusNutrients must not exceed 20 entries.");

            // ── Analyse ─────────────────────────────────────────────────────────

            var result = await _gapService.AnalyseGapsAsync(request, ct);
            sw.Stop();

            // ── Phase 2 safety metadata ──────────────────────────────────────────

            var alerts = new List<SafetyAlertDto>();

            // Unmodelled allergens — gaps may be underestimated
            if (result.UnmodelledAllergens.Any())
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code     = "UNMODELLED_ALLERGENS",
                    Message  = $"{result.UnmodelledAllergens.Count} allergen(s) are not in the gap-analysis " +
                               $"knowledge base and their dietary impact was not modelled: " +
                               $"{string.Join(", ", result.UnmodelledAllergens)}. " +
                               "Nutrient gaps may be underestimated for these allergens."
                });
            }

            // Critical overall risk
            if (result.OverallRisk == "CRITICAL")
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code     = "CRITICAL_NUTRITIONAL_RISK",
                    Message  = "The submitted allergen combination creates CRITICAL nutritional gaps " +
                               "— multiple high-severity nutrient deficiency risks have been identified. " +
                               "Dietitian involvement is strongly recommended before patient guidance is issued."
                });
            }
            else if (result.OverallRisk == "HIGH")
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code     = "HIGH_NUTRITIONAL_RISK",
                    Message  = "The submitted allergen profile creates HIGH-severity nutritional gaps. " +
                               "A registered dietitian should review supplementation and dietary planning."
                });
            }

            // No gaps found
            if (result.TotalGapsIdentified == 0)
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code     = "NO_GAPS_IDENTIFIED",
                    Message  = "No nutrient gaps were identified for the submitted allergen profile. " +
                               "This result is based on the knowledge base of modelled allergens only — " +
                               "a clinical nutritional assessment may reveal additional considerations."
                });
            }

            // Nickel filter active
            if (request.NickelSensitive)
            {
                int nickelExcluded = result.NutrientGaps.Sum(g => g.NickelExcludedCount);
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code     = "NICKEL_FILTER_ACTIVE",
                    Message  = $"Nickel sensitivity filtering was applied (Rebelytics v9.1.1). " +
                               $"{nickelExcluded} food(s) were excluded across all gaps due to moderate/high " +
                               "nickel content. Foods with no nickel data are included but flagged with " +
                               "nickelDataMissing = true."
                });
            }

            // Foods missing from database
            int unmatched = result.NutrientGaps.Sum(g => g.CompensatingFoods.Count(f => !f.MatchedInDatabase));
            if (unmatched > 0)
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code     = "FOODS_NOT_IN_DATABASE",
                    Message  = $"{unmatched} compensating food recommendation(s) across all gaps could not be " +
                               "verified in the GlobalIngredients database. These are included based on the " +
                               "knowledge base but allergen safety is not database-validated for these items."
                });
            }

            // Macros data availability notice
            alerts.Add(new SafetyAlertDto
            {
                Severity = "INFO",
                Code     = "MACROS_DATA_SCOPE",
                Message  = "USDA macronutrient data (calories, protein, carbs, fat, fiber) is included " +
                           "where available in NutritionCache. Micronutrient content (e.g., calcium mg, " +
                           "vitamin D IU) is not quantified — gap analysis is based on food category " +
                           "inclusion/exclusion modelling, not per-serving nutrient calculations."
            });

            // Standard clinical disclaimer alert
            alerts.Add(new SafetyAlertDto
            {
                Severity = "INFO",
                Code     = "CLINICAL_DISCLAIMER",
                Message  = "This analysis is intended as a clinical decision-support tool, not a " +
                           "personalised dietary prescription. Nutrient requirements vary by age, sex, " +
                           "pregnancy status, and health conditions. Always involve a registered dietitian " +
                           "for individual patient guidance."
            });

            bool hasWarnings = alerts.Any(a => a.Severity == "WARNING");

            // Confidence scales with overall risk and unmodelled allergens
            int confidenceScore = result.UnmodelledAllergens.Any() ? 65 : 85;
            string confidenceLevel = confidenceScore >= 80 ? "HIGH" : "MEDIUM";

            var safetyMeta = new SafetyMeta
            {
                Disclaimer =
                    "Nutritional gap analysis is based on population-level dietary science and food category " +
                    "modelling — it does not account for individual variation in absorption, supplementation, " +
                    "or dietary history. Compensating food recommendations are filtered for allergen safety " +
                    "against the Platerly GlobalIngredients database; however, individual product formulations " +
                    "should always be verified with the manufacturer. This output is not a substitute for " +
                    "individualised dietary assessment by a qualified healthcare professional. " +
                    "Recipzee / Platerly Inc. accepts no liability for clinical decisions made on the basis " +
                    "of this analysis.",
                Confidence = new ConfidenceInfo
                {
                    Level       = confidenceLevel,
                    Score       = confidenceScore,
                    Explanation = $"{result.TotalGapsIdentified} nutrient gap(s) identified across " +
                                  $"{request.AllergenProfile.Count} allergen(s). " +
                                  $"{result.UnmodelledAllergens.Count} allergen(s) not in knowledge base. " +
                                  $"Overall risk: {result.OverallRisk}."
                },
                DataFreshness = new DataFreshnessInfo
                {
                    SourceVersion  = "Platerly Nutrition Knowledge Base v1.0 + Rebelytics v9.1.1",
                    SourceYear     = 2025,
                    SourceCitation = "Nutrient risk mappings derived from Dietary Reference Intakes (DRI) — " +
                                     "Institute of Medicine / National Academies. Food category data from " +
                                     "Platerly GlobalIngredients. Nickel data from Rebelytics v9.1.1 " +
                                     "(September 2025). USDA macros from FoodData Central.",
                    IsStale        = false,
                    LastImportedAt = _config["B2BApi:AllergenDataLastImportedAt"] ?? "2026-01-15"
                },
                Alerts      = alerts,
                GeneratedAt = DateTime.UtcNow
            };

            var response = new SafetyApiResponse<NutritionGapResult>
            {
                Data   = result,
                Safety = safetyMeta
            };

            // ── Audit ────────────────────────────────────────────────────────────

            var apiKeyId = GetApiKeyId();

            _ = _audit.LogAsync(new SafetyAuditLog
            {
                HttpMethod          = "POST",
                Endpoint            = "/api/b2b/nutrition/gap-analysis",
                ApiKeyId            = apiKeyId == Guid.Empty ? null : apiKeyId,
                IpAddress           = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent           = HttpContext.Request.Headers.UserAgent.ToString() is { Length: > 512 } ua
                                        ? ua[..512] : HttpContext.Request.Headers.UserAgent.ToString(),
                ResponseStatusCode  = 200,
                ResponseTimeMs      = sw.ElapsedMilliseconds,
                AlertCount          = alerts.Count,
                HighestAlertSeverity = hasWarnings ? "WARNING" : "INFO",
                IngredientContext   = string.Join(", ", request.AllergenProfile.Take(20)) is { Length: > 1024 } ic
                                        ? ic[..1024] : string.Join(", ", request.AllergenProfile.Take(20))
            }, CancellationToken.None);

            return Ok(response);
        }
    }
}
