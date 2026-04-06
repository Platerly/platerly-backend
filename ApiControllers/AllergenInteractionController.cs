using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Models;
using Platerly.Api.Services.Allergy;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Cross-Allergen Interaction Engine. Analyses a multi-allergen patient profile for
    /// cross-reactive allergen pairs and computes a compound systemic risk score.
    ///
    /// Base route: /api/b2b/allergens
    /// Auth requirement: API Key (<c>ApiKeyAuthenticationOptions.DefaultScheme</c>).
    ///
    /// Cross-reactivity knowledge base:
    ///   12 established immunological pair relationships drawn from:
    ///   Sicherer 2001 (J Allergy Clin Immunol), EAACI Food Allergy and Anaphylaxis Guidelines,
    ///   and the PRACTALL consensus report.
    ///   Covered pairs include: Peanut–Sesame (2S albumin), Milk–Egg co-sensitisation,
    ///   Fish–Shellfish, Peanut–Tree Nuts, Mustard–Sesame, Milk–Soy, and more.
    ///
    /// Compound risk score (0–100):
    ///   base score (allergen count) + cross-reactivity bonus + anaphylaxis-prone allergen bonus.
    ///   Risk bands: STANDARD | ELEVATED | HIGH | SEVERE.
    ///
    /// POST /api/b2b/allergens/interaction-analysis — requires ≥2 allergens (up to 30).
    ///
    /// Applies the full Phase 2 safety layer. All requests audit-logged fire-and-forget.
    /// </summary>
    [ApiController]
    [Route("api/b2b/allergens")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class AllergenInteractionController : B2BControllerBase
    {
        private readonly IAllergenInteractionService _interactionService;
        private readonly ISafetyAuditService _audit;
        private readonly ILogger<AllergenInteractionController> _logger;
        private readonly IConfiguration _config;

        public AllergenInteractionController(
            IAllergenInteractionService interactionService,
            ISafetyAuditService audit,
            ISafetyMetaService safetyMeta,
            IConfiguration config,
            ILogger<AllergenInteractionController> logger)
            : base(safetyMeta)
        {
            _interactionService = interactionService;
            _audit = audit;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Analyse cross-reactive allergen interactions for a multi-allergen patient profile.
        ///
        /// Returns:
        ///   • crossReactivePairs — all detected cross-reactive allergen pairs with:
        ///       mechanism, co-sensitisation likelihood %, clinical significance, clinical note
        ///   • compoundRiskScore — 0–100 aggregate score (base + cross-reactivity + anaphylaxis risk)
        ///   • riskClassification — STANDARD / ELEVATED / HIGH / SEVERE
        ///   • clinicalRecommendations — actionable per-risk-band and per-pair guidance
        ///   • combinedSafeFoods — foods verified safe against ALL submitted allergens simultaneously
        ///   • effectiveAllergenFilter — the full set of allergen types used to compute the safe window
        ///
        /// Cross-reactivity knowledge base covers 12 pair relationships including:
        ///   Peanut–Sesame (2S albumin), Milk–Eggs (co-sensitisation), Fish–Shellfish,
        ///   Peanut–Tree Nuts, Mustard–Sesame, Milk–Soy, and more.
        ///
        /// NOTE: Cross-reactivity findings are probabilistic. A positive pair detection
        /// indicates elevated risk, not certainty. Confirm with allergen-specific IgE testing
        /// or supervised oral food challenge before implementing cross-reactive avoidance.
        /// </summary>
        [HttpPost("interaction-analysis")]
        public async Task<ActionResult> AnalyseInteractions(
            [FromBody] AllergenInteractionRequest request,
            CancellationToken ct = default)
        {
            if (!HasScope("clinical"))
                return ApiError(
                    "The 'clinical' scope is required for this endpoint. " +
                    "Upgrade to the Clinical Research tier at recipzee.com/api",
                    403);

            var sw = Stopwatch.StartNew();

            // ── Validate ────────────────────────────────────────────────────────

            if (request.Allergens == null || request.Allergens.Count == 0)
                return ApiError("At least one allergen is required.");

            if (request.Allergens.Count < 2)
                return ApiError("At least 2 allergens are required for interaction analysis.");

            if (request.Allergens.Count > 30)
                return ApiError("Maximum 30 allergens per request.");

            var nonEmpty = request.Allergens.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            if (nonEmpty.Count < 2)
                return ApiError("At least 2 non-empty allergen names are required.");

            // ── Run analysis ─────────────────────────────────────────────────────

            var result = await _interactionService.AnalyseInteractionsAsync(request, ct);
            sw.Stop();

            // ── Phase 2 safety metadata ──────────────────────────────────────────

            var alerts = new List<SafetyAlertDto>();

            // FATAL: majority of submitted allergens not recognised
            if (result.UnknownAllergens.Any())
            {
                decimal unknownPct = (decimal)result.UnknownAllergens.Count / request.Allergens.Count * 100;

                if (unknownPct >= 50)
                {
                    alerts.Add(new SafetyAlertDto
                    {
                        Severity = "FATAL",
                        Code = "MOSTLY_UNKNOWN_ALLERGENS",
                        Message = $"{result.UnknownAllergens.Count} of {request.Allergens.Count} submitted allergens " +
                                  $"({unknownPct:F0}%) are not in the interaction knowledge base: " +
                                  string.Join(", ", result.UnknownAllergens) +
                                  ". Cross-reactivity analysis and the safe food window are significantly incomplete. " +
                                  "Do not use this result clinically without resolving unknown allergens."
                    });
                }
                else
                {
                    alerts.Add(new SafetyAlertDto
                    {
                        Severity = "WARNING",
                        Code = "UNKNOWN_ALLERGENS",
                        Message = $"{result.UnknownAllergens.Count} allergen(s) were not recognised: " +
                                  string.Join(", ", result.UnknownAllergens) +
                                  ". They are excluded from cross-reactivity pair matching and the safe food window. " +
                                  "Foods that trigger these allergens may appear safe erroneously."
                    });
                }
            }

            // WARNING: severe risk classification
            if (result.RiskClassification == "SEVERE")
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code = "SEVERE_COMPOUND_RISK",
                    Message = $"Compound risk score of {result.CompoundRiskScore}/100 classifies this allergen " +
                              "profile as SEVERE. Multiple cross-reactive pairs involving anaphylaxis-prone allergens " +
                              "are present. Specialist allergenist management and epinephrine availability are strongly recommended."
                });
            }
            else if (result.RiskClassification == "HIGH")
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code = "HIGH_COMPOUND_RISK",
                    Message = $"Compound risk score of {result.CompoundRiskScore}/100 (HIGH). " +
                              "Significant dietary restriction and nutritional deficiency risk. Dietitian referral recommended."
                });
            }

            // INFO: cross-reactive pairs detected
            if (result.CrossReactivePairs.Any(p => p.ClinicalSignificance == "HIGH"))
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code = "HIGH_SIGNIFICANCE_PAIRS",
                    Message = $"{result.CrossReactivePairs.Count(p => p.ClinicalSignificance == "HIGH")} high-significance " +
                              "cross-reactive pair(s) detected. Clinical cross-reactivity is probable — consider confirmatory " +
                              "allergen-specific IgE testing or supervised oral food challenge before implementing avoidance."
                });
            }
            else if (result.CrossReactivePairs.Any())
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "CROSS_REACTIVE_PAIRS_DETECTED",
                    Message = $"{result.CrossReactivePairs.Count} cross-reactive or co-sensitised allergen pair(s) detected. " +
                              "See crossReactivePairs for mechanism and clinical guidance per pair."
                });
            }
            else
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "NO_KNOWN_INTERACTIONS",
                    Message = "No cross-reactive allergen pairs were detected within the submitted allergen profile. " +
                              "Each allergen should be managed independently. " +
                              "Compound risk is driven by allergen count and individual anaphylaxis risk."
                });
            }

            // INFO: cross-reactivity is probabilistic
            alerts.Add(new SafetyAlertDto
            {
                Severity = "INFO",
                Code = "PROBABILISTIC_FINDINGS",
                Message = "Cross-reactivity co-sensitisation percentages represent population-level estimates " +
                          "from peer-reviewed studies. An individual patient may or may not react to a cross-reactive " +
                          "allergen. Confirm clinically before implementing avoidance beyond confirmed allergens."
            });

            // ── Confidence score ─────────────────────────────────────────────────

            bool hasFatal    = alerts.Any(a => a.Severity == "FATAL");
            bool hasWarnings = alerts.Any(a => a.Severity == "WARNING");

            int recognisedPct = request.Allergens.Count > 0
                ? result.RecognisedAllergens.Count * 100 / request.Allergens.Count
                : 0;

            int baseScore = recognisedPct;
            if (hasWarnings) baseScore = Math.Max(baseScore - 15, 0);
            if (hasFatal)    baseScore = Math.Max(baseScore - 35, 0);
            baseScore = Math.Clamp(baseScore, 0, 100);
            string confidenceLevel = baseScore >= 70 ? "HIGH" : baseScore >= 40 ? "MEDIUM" : "LOW";

            var safetyMeta = new SafetyMeta
            {
                Disclaimer =
                    "Cross-reactivity findings are generated from an embedded immunological knowledge base " +
                    "and represent population-level probabilistic relationships, not individual diagnoses. " +
                    "The compound risk score and clinical recommendations are for informational reference only — " +
                    "they do not constitute medical advice and must be interpreted by a qualified healthcare professional. " +
                    "Confirm cross-reactive allergen avoidance requirements with allergen-specific IgE testing " +
                    "or supervised oral food challenge. Recipzee / Platerly Inc. accepts no liability for " +
                    "adverse reactions arising from reliance on this analysis.",
                Confidence = new ConfidenceInfo
                {
                    Level = confidenceLevel,
                    Score = baseScore,
                    Explanation = $"{result.RecognisedAllergens.Count}/{request.Allergens.Count} allergens recognised " +
                                  $"({recognisedPct}%). {result.CrossReactivePairs.Count} cross-reactive pair(s) detected " +
                                  $"from 12-pair knowledge base. Compound risk score: {result.CompoundRiskScore}/100." +
                                  (hasWarnings ? " Quality warnings present." : "") +
                                  $" Confidence: {baseScore}/100."
                },
                DataFreshness = new DataFreshnessInfo
                {
                    SourceVersion = "Platerly Cross-Reactivity Knowledge Base v1.0",
                    SourceYear = 2024,
                    SourceCitation =
                        "Sicherer SH. Manifestations of food allergy: evaluation and management. Am Fam Physician. 1999. " +
                        "Sicherer SH. Clinical implications of cross-reactive food allergens. J Allergy Clin Immunol. 2001. " +
                        "EAACI Food Allergy and Anaphylaxis Guidelines: diagnosis and management. Allergy. 2014. " +
                        "PRACTALL report: Food allergy in children. Allergy. 2008. " +
                        "Beyer K et al. Tree nut allergy. Curr Allergy Asthma Rep. 2001.",
                    IsStale = false,
                    LastImportedAt = _config["B2BApi:AllergenDataLastImportedAt"] ?? "2026-01-15"
                },
                Alerts = alerts,
                GeneratedAt = DateTime.UtcNow
            };

            var response = new SafetyApiResponse<AllergenInteractionResult>
            {
                Data   = result,
                Safety = safetyMeta
            };

            // ── Audit log ────────────────────────────────────────────────────────

            var apiKeyId = GetApiKeyId();
            var highestSeverity = hasFatal ? "FATAL" : hasWarnings ? "WARNING"
                : alerts.Any(a => a.Severity == "INFO") ? "INFO" : null;

            var allergyCtx = string.Join(", ", request.Allergens);

            _ = _audit.LogAsync(new SafetyAuditLog
            {
                HttpMethod = "POST",
                Endpoint = "/api/b2b/allergens/interaction-analysis",
                ApiKeyId = apiKeyId == Guid.Empty ? null : apiKeyId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString() is { Length: > 512 } ua
                    ? ua[..512] : HttpContext.Request.Headers.UserAgent.ToString(),
                ResponseStatusCode = 200,
                ResponseTimeMs = sw.ElapsedMilliseconds,
                AlertCount = alerts.Count,
                HighestAlertSeverity = highestSeverity,
                AllergyContext = allergyCtx.Length > 512
                    ? allergyCtx[..512] : allergyCtx,
                ConfidenceLevel = confidenceLevel
            }, CancellationToken.None);

            return Ok(response);
        }
    }
}
