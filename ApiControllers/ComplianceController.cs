using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Models;
using Platerly.Api.Services.Compliance;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Compliance API — Canadian food labelling tools.
    ///
    /// Base route: /api/b2b/compliance
    /// Auth requirement: API Key (<c>ApiKeyAuthenticationOptions.DefaultScheme</c>).
    ///
    /// Regulatory context:
    ///   Canadian food manufacturers are required to declare priority allergens on pre-packaged
    ///   foods under the Safe Food for Canadians Act (SFCA S.C. 2012, c. 24) and
    ///   Safe Food for Canadians Regulations (SFCR SOR/2018-108, s.257).
    ///   The CFIA recognises 11 priority allergens: peanuts, tree nuts, sesame seeds, milk,
    ///   eggs, fish, shellfish, wheat and triticale, soy, mustard, and sulphites.
    ///
    /// POST /api/b2b/compliance/cfia-allergen-declaration — generates bilingual "Contains /
    ///   Contient" and "May contain / Peut contenir" declaration text for product packaging.
    ///
    /// Detection uses GlobalIngredient structured allergen records first, with a keyword lexicon
    /// fallback for ingredients not in the database. Keyword matches are flagged separately in
    /// the response so consumers can assess detection confidence.
    ///
    /// Applies the full Phase 2 safety layer with confidence scoring and audit logging.
    ///
    /// IMPORTANT: Output must be verified by a qualified food safety professional before
    /// use on commercial packaging. Compliance with SFCR is the manufacturer's responsibility.
    /// </summary>
    [ApiController]
    [Route("api/b2b/compliance")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class ComplianceController : B2BControllerBase
    {
        private readonly ICfiaAllergenDeclarationService _cfiaService;
        private readonly ISafetyAuditService _audit;
        private readonly ILogger<ComplianceController> _logger;

        public ComplianceController(
            ICfiaAllergenDeclarationService cfiaService,
            ISafetyAuditService audit,
            ISafetyMetaService safetyMeta,
            ILogger<ComplianceController> logger)
            : base(safetyMeta)
        {
            _cfiaService = cfiaService;
            _audit = audit;
            _logger = logger;
        }

        /// <summary>
        /// Generate a bilingual CFIA allergen declaration for a product's ingredient list.
        ///
        /// Returns:
        ///   • "Contains: ..." / "Contient : ..." statement listing all 11 CFIA priority allergens
        ///     definitively present in the ingredient list.
        ///   • "May contain: ..." / "Peut contenir : ..." statement for cross-contact allergens.
        ///   • A combined bilingual label block ready to copy-paste onto packaging.
        ///   • Per-ingredient allergen breakdown identifying which ingredient triggered each allergen.
        ///   • Unmatched ingredient list — ingredients for which allergen data could not be found.
        ///
        /// CFIA 11 Priority Allergens (Safe Food for Canadians Regulations):
        ///   Peanuts, Tree nuts, Sesame seeds, Milk, Eggs, Fish, Shellfish,
        ///   Wheat and triticale, Soy, Mustard, Sulphites.
        ///
        /// Detection: GlobalIngredient structured allergen records first, with a keyword-lexicon
        /// fallback for ingredients not in the database.
        ///
        /// IMPORTANT: This output is generated for reference purposes only and must be verified
        /// by a qualified food safety professional before use on commercial packaging.
        /// </summary>
        [HttpPost("cfia-allergen-declaration")]
        public async Task<ActionResult> GenerateCfiaDeclaration(
            [FromBody] CfiaAllergenDeclarationRequest request,
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            // ── Validate ────────────────────────────────────────────────────────

            if (request.Ingredients == null || request.Ingredients.Count == 0)
                return ApiError("At least one ingredient is required.");

            if (request.Ingredients.Count > 200)
                return ApiError("Maximum 200 ingredients per request.");

            var validIngredients = request.Ingredients
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToList();

            if (!validIngredients.Any())
                return ApiError("Ingredient list must contain at least one non-empty name.");

            if (request.ProductName?.Length > 256)
                return ApiError("productName must not exceed 256 characters.");

            // ── Detect allergens ─────────────────────────────────────────────────

            var result = await _cfiaService.GenerateDeclarationAsync(request, ct);
            sw.Stop();

            // ── Phase 2 safety metadata ──────────────────────────────────────────

            var alerts = new List<SafetyAlertDto>();

            // FATAL: high unmatched rate undermines declaration completeness
            if (result.TotalIngredients > 0)
            {
                decimal unmatchedPct = (decimal)result.UnmatchedIngredients.Count / result.TotalIngredients * 100;

                if (unmatchedPct >= 30)
                {
                    alerts.Add(new SafetyAlertDto
                    {
                        Severity = "FATAL",
                        Code = "HIGH_UNMATCHED_RATE",
                        Message = $"{result.UnmatchedIngredients.Count} of {result.TotalIngredients} ingredients " +
                                  $"({unmatchedPct:F0}%) could not be matched to the allergen database. " +
                                  "The allergen declaration may be INCOMPLETE. Allergens present in unmatched " +
                                  "ingredients will not appear in the declaration. Do not use this output on " +
                                  "commercial packaging without manually verifying all unmatched ingredients."
                    });
                }
                else if (result.UnmatchedIngredients.Any())
                {
                    alerts.Add(new SafetyAlertDto
                    {
                        Severity = "WARNING",
                        Code = "UNMATCHED_INGREDIENTS",
                        Message = $"{result.UnmatchedIngredients.Count} ingredient(s) could not be matched to " +
                                  "the allergen database: " +
                                  string.Join(", ", result.UnmatchedIngredients.Take(10)) +
                                  ". Their allergen content is excluded from this declaration — actual allergen " +
                                  "presence may be higher. Verify these ingredients manually before labelling."
                    });
                }
            }

            // WARNING: keyword-only matches are less reliable than structured data
            if (result.DetectionSummary.KeywordMatches > 0)
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code = "KEYWORD_DETECTION_USED",
                    Message = $"{result.DetectionSummary.KeywordMatches} ingredient(s) were matched via keyword " +
                              "lexicon only (not confirmed by structured allergen records). Keyword detection may " +
                              "produce false positives or miss allergens in processed forms. Review ingredient " +
                              "specifications before finalising the declaration."
                });
            }

            // INFO: no allergens detected
            if (!result.ContainsAllergens.Any() && !result.MayContainAllergens.Any() &&
                result.UnmatchedIngredients.Count == 0)
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "NO_ALLERGENS_DETECTED",
                    Message = "No CFIA priority allergens were detected in this ingredient list. " +
                              "If the product is manufactured in a facility handling allergens, " +
                              "a precautionary may-contain statement should be added manually."
                });
            }

            // INFO: may-contain detected — recommend advisory statement
            if (result.MayContainAllergens.Any())
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "CROSS_CONTACT_ADVISORY",
                    Message = "Cross-contact or may-contain risk detected for " +
                              string.Join(", ", result.MayContainAllergens.Select(a => a.EnglishName)) +
                              ". The precautionary may-contain statement is included in the declaration. " +
                              "Verify actual manufacturing facility practices before finalising."
                });
            }

            // INFO: Gluten/wheat specificity note
            if (result.ContainsAllergens.Any(a => a.AllergenType == "Gluten") ||
                result.MayContainAllergens.Any(a => a.AllergenType == "Gluten"))
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "GLUTEN_SOURCE_SPECIFICITY",
                    Message = "CFIA regulations require listing the specific gluten-containing grain " +
                              "(wheat, rye, barley, oats, or triticale). This declaration uses " +
                              "\"Wheat and triticale\" as the canonical label — replace with the specific " +
                              "grain(s) present in your formulation if different."
                });
            }

            // ── Confidence score ─────────────────────────────────────────────────

            bool hasFatal = alerts.Any(a => a.Severity == "FATAL");
            bool hasWarnings = alerts.Any(a => a.Severity == "WARNING");

            int matchedPct = result.TotalIngredients > 0
                ? result.MatchedIngredients * 100 / result.TotalIngredients
                : 0;

            int baseScore = matchedPct;
            if (hasWarnings) baseScore = Math.Max(baseScore - 20, 0);
            if (hasFatal)    baseScore = Math.Max(baseScore - 40, 0);
            baseScore = Math.Clamp(baseScore, 0, 100);

            string confidenceLevel = baseScore >= 70 ? "HIGH" : baseScore >= 40 ? "MEDIUM" : "LOW";

            var safetyMeta = new SafetyMeta
            {
                Disclaimer =
                    "This output is generated for reference purposes only and should be verified by a " +
                    "qualified food safety professional before use on commercial packaging. " +
                    "Allergen detection is based on ingredient name matching against the Platerly/Recipzee " +
                    "allergen database and keyword lexicon — it does not account for processing aids, " +
                    "carry-over allergens, or undeclared ingredients. Compliance with the Safe Food for " +
                    "Canadians Regulations (SFCR) is the responsibility of the manufacturer. " +
                    "Recipzee / Platerly Inc. accepts no liability for mislabelling arising from reliance on this output.",
                Confidence = new ConfidenceInfo
                {
                    Level = confidenceLevel,
                    Score = baseScore,
                    Explanation = $"{result.MatchedIngredients}/{result.TotalIngredients} ingredients matched " +
                                  $"({matchedPct}%); {result.DetectionSummary.StructuredMatches} via structured data, " +
                                  $"{result.DetectionSummary.KeywordMatches} via keyword lexicon, " +
                                  $"{result.DetectionSummary.UnmatchedCount} unmatched." +
                                  (hasWarnings ? " Quality warnings present." : "") +
                                  $" Composite score: {baseScore}/100."
                },
                DataFreshness = new DataFreshnessInfo
                {
                    SourceVersion = "Platerly Allergen Database v1.0",
                    SourceYear = 2025,
                    SourceCitation =
                        "Safe Food for Canadians Act (SFCA) S.C. 2012, c. 24. " +
                        "Safe Food for Canadians Regulations (SFCR) SOR/2018-108, s.257. " +
                        "Health Canada priority allergen list (2024): peanuts, tree nuts, sesame seeds, " +
                        "milk, eggs, fish, shellfish, wheat and triticale, soy, mustard, sulphites.",
                    IsStale = false,
                    LastImportedAt = "2026-01-15"
                },
                Alerts = alerts,
                GeneratedAt = DateTime.UtcNow
            };

            var response = new SafetyApiResponse<CfiaAllergenDeclarationResult>
            {
                Data = result,
                Safety = safetyMeta
            };

            // ── Audit log ────────────────────────────────────────────────────────

            var apiKeyId = GetApiKeyId();
            var highestSeverity = hasFatal ? "FATAL" : hasWarnings ? "WARNING"
                : alerts.Any(a => a.Severity == "INFO") ? "INFO" : null;

            var allergenContext = string.Join(", ",
                result.ContainsAllergens.Select(a => a.AllergenType)
                .Concat(result.MayContainAllergens.Select(a => $"mc:{a.AllergenType}")));

            var ingredientContext = string.Join(", ",
                validIngredients.Take(20));

            _ = _audit.LogAsync(new SafetyAuditLog
            {
                HttpMethod = "POST",
                Endpoint = "/api/b2b/compliance/cfia-allergen-declaration",
                ApiKeyId = apiKeyId == Guid.Empty ? null : apiKeyId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString().Length > 512
                    ? HttpContext.Request.Headers.UserAgent.ToString()[..512]
                    : HttpContext.Request.Headers.UserAgent.ToString(),
                ResponseStatusCode = 200,
                ResponseTimeMs = sw.ElapsedMilliseconds,
                AlertCount = alerts.Count,
                HighestAlertSeverity = highestSeverity,
                IngredientContext = ingredientContext.Length > 1024
                    ? ingredientContext[..1024]
                    : ingredientContext,
                AllergyContext = allergenContext.Length > 512
                    ? allergenContext[..512]
                    : allergenContext,
                ConfidenceLevel = confidenceLevel
            }, CancellationToken.None);

            return Ok(response);
        }
    }
}
