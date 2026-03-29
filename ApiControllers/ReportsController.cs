using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Models;
using Platerly.Api.Services.Reports;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Patient Reports API.
    ///
    /// POST /api/b2b/reports/patient-allergen-report
    ///   Generates a white-label clinical PDF allergen report for a patient.
    ///   Includes allergen profile, foods to avoid, substitution guide,
    ///   verified safe food list, and optional nickel load assessment.
    ///   Requires the "reports" scope on the API key (Professional tier).
    ///
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/b2b/reports")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class ReportsController : B2BControllerBase
    {
        private readonly IPatientAllergenReportService _reportService;
        private readonly ISafetyAuditService _audit;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(
            IPatientAllergenReportService reportService,
            ISafetyAuditService audit,
            ISafetyMetaService safetyMeta,
            ILogger<ReportsController> logger)
            : base(safetyMeta)
        {
            _reportService = reportService;
            _audit = audit;
            _logger = logger;
        }

        /// <summary>
        /// Generate a white-label clinical allergen report as a PDF.
        ///
        /// The report contains:
        ///   • Patient information block (name, DOB, chart number, clinical notes)
        ///   • Confirmed allergen profile table with clinical guidance per allergen
        ///   • Foods to avoid — ingredients confirmed to contain patient's allergens,
        ///     with safe substitutes shown inline
        ///   • Safe foods grid — verified allergen-free options
        ///   • Nickel load section — when NickelMealPlan is provided, includes daily
        ///     nickel intake vs the 150 μg/day threshold and top contributing foods
        ///   • Mandatory clinical disclaimer
        ///
        /// The PDF is returned as a base64-encoded string in the response body.
        /// Decode and serve with Content-Type: application/pdf.
        ///
        /// SCOPE GATE: API key must include the "reports" scope.
        /// Contact Platerly to enable the reports scope on your key.
        /// </summary>
        [HttpPost("patient-allergen-report")]
        public async Task<ActionResult> GeneratePatientReport(
            [FromBody] PatientAllergenReportRequest request,
            CancellationToken ct = default)
        {
            // ── Scope gate (Professional tier) ──────────────────────────────────
            if (!HasScope("reports"))
            {
                return ApiError(
                    "The 'reports' scope is required to access this endpoint. " +
                    "Upgrade to the Professional tier and request the 'reports' scope from Platerly support.",
                    403);
            }

            var sw = Stopwatch.StartNew();

            // ── Validate ────────────────────────────────────────────────────────

            if (string.IsNullOrWhiteSpace(request.Patient.Name))
                return ApiError("patient.name is required.");

            if (string.IsNullOrWhiteSpace(request.Clinic.Name))
                return ApiError("clinic.name is required.");

            if (request.AllergenProfile == null || request.AllergenProfile.Count == 0)
                return ApiError("At least one allergen in allergenProfile is required.");

            if (request.AllergenProfile.Count > 20)
                return ApiError("Maximum 20 allergens per report.");

            if (!string.IsNullOrWhiteSpace(request.Clinic.LogoBase64) &&
                request.Clinic.LogoBase64.Length > 280_000) // ~200 KB in base64
            {
                return ApiError("clinic.logoBase64 must not exceed 200 KB.");
            }

            if (request.NickelMealPlan != null)
            {
                if (request.NickelMealPlan.Meals == null || request.NickelMealPlan.Meals.Count == 0)
                    return ApiError("nickelMealPlan.meals must contain at least one meal when provided.");

                if (request.NickelMealPlan.Meals.Count > 10)
                    return ApiError("nickelMealPlan.meals must not exceed 10 meals.");
            }

            // ── Generate report ──────────────────────────────────────────────────

            var result = await _reportService.GenerateReportAsync(request, ct);
            sw.Stop();

            // ── Phase 2 safety metadata ──────────────────────────────────────────

            var alerts = new List<SafetyAlertDto>();

            // FATAL: completely unrecognised allergens reduce report reliability
            if (result.UnknownAllergens.Any())
            {
                decimal unknownPct = (decimal)result.UnknownAllergens.Count / request.AllergenProfile.Count * 100;

                if (unknownPct >= 50)
                {
                    alerts.Add(new SafetyAlertDto
                    {
                        Severity = "FATAL",
                        Code = "UNKNOWN_ALLERGENS",
                        Message = $"{result.UnknownAllergens.Count} of {request.AllergenProfile.Count} allergens " +
                                  $"were not found in the database: {string.Join(", ", result.UnknownAllergens)}. " +
                                  "The safe/unsafe food lists may be significantly incomplete. " +
                                  "Do not use this report clinically without resolving unrecognised allergens."
                    });
                }
                else
                {
                    alerts.Add(new SafetyAlertDto
                    {
                        Severity = "WARNING",
                        Code = "UNKNOWN_ALLERGENS",
                        Message = $"The following allergens were not found in the database and are excluded " +
                                  $"from safe/unsafe filtering: {string.Join(", ", result.UnknownAllergens)}. " +
                                  "Foods that trigger these allergens may appear in the safe list erroneously."
                    });
                }
            }

            // WARNING: very few safe foods found (possible incomplete allergen DB coverage)
            if (result.SafeFoodsCount == 0 && result.UnsafeFoodsCount == 0)
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code = "NO_FOOD_DATA",
                    Message = "No allergen-annotated foods were found for the submitted allergen profile. " +
                              "The food lists in this report are empty. Database coverage for these allergens may be limited."
                });
            }

            // INFO: nickel section included
            if (result.NickelSummary != null && result.NickelSummary.RiskLevel == "EXCEEDED")
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code = "NICKEL_THRESHOLD_EXCEEDED",
                    Message = $"Submitted meal plan exceeds the 150 μg/day nickel threshold " +
                              $"({result.NickelSummary.TotalNickelMcg} μg; {result.NickelSummary.ThresholdPercent}% of threshold). " +
                              "Advise patient to reduce high-nickel foods. See nickel load table in the report."
                });
            }

            // INFO: report size
            alerts.Add(new SafetyAlertDto
            {
                Severity = "INFO",
                Code = "REPORT_GENERATED",
                Message = $"PDF report generated: {result.FileSizeBytes:N0} bytes, " +
                          $"{result.SectionsIncluded.Count} sections, " +
                          $"{result.SafeFoodsCount} safe foods, {result.UnsafeFoodsCount} foods to avoid, " +
                          $"{result.SubstitutionSuggestionsCount} substitution suggestions."
            });

            // ── Confidence score ─────────────────────────────────────────────────

            bool hasFatal   = alerts.Any(a => a.Severity == "FATAL");
            bool hasWarnings = alerts.Any(a => a.Severity == "WARNING");

            int knownCount = request.AllergenProfile.Count - result.UnknownAllergens.Count;
            int matchedPct = request.AllergenProfile.Count > 0
                ? knownCount * 100 / request.AllergenProfile.Count
                : 100;

            int baseScore = matchedPct;
            if (hasWarnings) baseScore = Math.Max(baseScore - 20, 0);
            if (hasFatal)    baseScore = Math.Max(baseScore - 40, 0);
            baseScore = Math.Clamp(baseScore, 0, 100);
            string confidenceLevel = baseScore >= 70 ? "HIGH" : baseScore >= 40 ? "MEDIUM" : "LOW";

            var safetyMeta = new SafetyMeta
            {
                Disclaimer =
                    "This report is generated for clinical reference only and must be reviewed by a qualified " +
                    "healthcare professional before being used to make dietary recommendations. " +
                    "Allergen data is derived from the Platerly/Recipzee allergen database using ingredient " +
                    "name matching — it does not account for processing aids, carry-over allergens, or " +
                    "undeclared ingredients. Safe and unsafe food lists are indicative only. " +
                    "Recipzee / Platerly Inc. accepts no liability for adverse reactions arising from " +
                    "reliance on this report.",
                Confidence = new ConfidenceInfo
                {
                    Level = confidenceLevel,
                    Score = baseScore,
                    Explanation = $"{knownCount}/{request.AllergenProfile.Count} allergens recognised in the database " +
                                  $"({matchedPct}%). {result.SafeFoodsCount} safe foods, {result.UnsafeFoodsCount} unsafe foods found. " +
                                  (hasWarnings ? " Quality warnings present." : "") +
                                  $" Composite score: {baseScore}/100."
                },
                DataFreshness = new DataFreshnessInfo
                {
                    SourceVersion = "Platerly Allergen Database v1.0",
                    SourceYear = 2025,
                    SourceCitation =
                        "Allergen data sourced from the Platerly/Recipzee GlobalIngredientAllergen table, " +
                        "populated from FDA Big 9, EU Big 14, and CFIA priority allergen lists. " +
                        "Nickel data (where applicable): Rebelytics R&D Inc. v9.1.1, September 2025.",
                    IsStale = false,
                    LastImportedAt = "2026-01-15"
                },
                Alerts = alerts,
                GeneratedAt = DateTime.UtcNow
            };

            var response = new SafetyApiResponse<PatientAllergenReportResult>
            {
                Data   = result,
                Safety = safetyMeta
            };

            // ── Audit log ────────────────────────────────────────────────────────

            var apiKeyId = GetApiKeyId();
            var highestSeverity = hasFatal ? "FATAL" : hasWarnings ? "WARNING"
                : alerts.Any(a => a.Severity == "INFO") ? "INFO" : null;

            _ = _audit.LogAsync(new SafetyAuditLog
            {
                HttpMethod = "POST",
                Endpoint = "/api/b2b/reports/patient-allergen-report",
                ApiKeyId = apiKeyId == Guid.Empty ? null : apiKeyId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString() is { Length: > 512 } ua
                    ? ua[..512] : HttpContext.Request.Headers.UserAgent.ToString(),
                ResponseStatusCode = 200,
                ResponseTimeMs = sw.ElapsedMilliseconds,
                AlertCount = alerts.Count,
                HighestAlertSeverity = highestSeverity,
                AllergyContext = string.Join(", ", request.AllergenProfile).Length > 512
                    ? string.Join(", ", request.AllergenProfile)[..512]
                    : string.Join(", ", request.AllergenProfile),
                ConfidenceLevel = confidenceLevel
            }, CancellationToken.None);

            return Ok(response);
        }
    }
}
