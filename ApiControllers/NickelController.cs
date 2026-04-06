using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Models;
using Platerly.Api.Services.Nickel;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Dietary Nickel Load Calculator.
    ///
    /// Base route: /api/b2b/nickel
    /// Auth requirement: API Key (<c>ApiKeyAuthenticationOptions.DefaultScheme</c>).
    ///
    /// Calculates cumulative dietary nickel intake for a full day's meal plan and compares
    /// it against the 150 μg/day clinical threshold established by Mislankar &amp; Zirwas (2013)
    /// "Low Nickel Diet Scoring System for Systemic Nickel Allergy Syndrome".
    ///
    /// Clinical threshold bands:
    ///   SAFE     — ≤75 μg/day  (≤50% of 150 μg threshold)
    ///   MODERATE — 75–150 μg/day (50–100% of threshold)
    ///   EXCEEDED — >150 μg/day (threshold exceeded; SNAS flare risk)
    ///
    /// Data source: Rebelytics R&amp;D Inc. Summary Tables v9.1.1 (September 2025),
    /// compiled from 257 international food composition studies and 20,575 individual measurements.
    ///
    /// Applies the full Phase 2 safety layer with domain-specific confidence scoring.
    /// All requests are audit-logged fire-and-forget via <see cref="ISafetyAuditService"/>.
    /// </summary>
    [ApiController]
    [Route("api/b2b/nickel")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class NickelController : B2BControllerBase
    {
        private readonly INickelLoadService _nickelLoad;
        private readonly ISafetyAuditService _audit;
        private readonly ILogger<NickelController> _logger;
        private readonly IConfiguration _config;

        public NickelController(
            INickelLoadService nickelLoad,
            ISafetyAuditService audit,
            ISafetyMetaService safetyMeta,
            IConfiguration config,
            ILogger<NickelController> logger)
            : base(safetyMeta)
        {
            _nickelLoad = nickelLoad;
            _audit = audit;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Calculates cumulative daily nickel intake across all meals in a day.
        ///
        /// Quantity scaling:
        ///   nickelMcg = (quantityGrams / referenceServingGrams) × nickelPerServing
        ///   When <c>quantityGrams</c> is omitted, one reference serving is assumed and an
        ///   ASSUMED_SERVING_SIZES INFO alert is added.
        ///
        /// Risk levels (Mislankar &amp; Zirwas 2013, 150 μg/day threshold):
        ///   SAFE     — ≤75 μg  (≤50% of threshold)
        ///   MODERATE — 75–150 μg (50–100%)
        ///   EXCEEDED — >150 μg   (threshold exceeded; THRESHOLD_EXCEEDED WARNING alert generated)
        ///
        /// Phase 2 safety alerts appended to the response:
        ///   FATAL   — HIGH_UNMATCHED_RATE (≥30% unmatched ingredients)
        ///   WARNING — UNMATCHED_INGREDIENTS (partial), THRESHOLD_EXCEEDED, HIGH_VARIABILITY (per ingredient)
        ///   INFO    — APPROACHING_THRESHOLD (MODERATE risk), PREPARATION_NOTE (per ingredient),
        ///             ASSUMED_SERVING_SIZES
        ///
        /// Confidence score (0–100) is computed from:
        ///   matched ingredient percentage − warning penalties − fatal penalties − low-confidence-data penalty.
        ///   FATAL alert → −40, WARNING alert → −20, >50% low-confidence contributors → −15.
        /// </summary>
        /// <param name="request">
        /// <c>Meals</c>: 1–10 meals, each with a label and 1–50 ingredients.
        /// Each ingredient may include <c>quantityGrams</c> (positive, ≤5000 g).
        /// </param>
        /// <returns>
        /// 200 with <see cref="SafetyApiResponse{NickelDailyLoadResponse}"/> including
        /// total nickel load, risk level, per-meal breakdown, top contributors, and unmatched list;
        /// 400 on validation failures.
        /// Side effects: fire-and-forget audit log via <see cref="ISafetyAuditService"/>.
        /// </returns>
        [HttpPost("daily-load")]
        public async Task<ActionResult> CalculateDailyLoad(
            [FromBody] NickelDailyLoadRequest request,
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            // ── Validate ─────────────────────────────────────────────────────────

            if (request.Meals == null || request.Meals.Count == 0)
                return ApiError("At least one meal is required.");

            if (request.Meals.Count > 10)
                return ApiError("Maximum 10 meals per daily-load request.");

            foreach (var meal in request.Meals)
            {
                if (string.IsNullOrWhiteSpace(meal.Label))
                    return ApiError("Each meal must have a non-empty label.");

                if (meal.Ingredients == null || meal.Ingredients.Count == 0)
                    return ApiError($"Meal '{meal.Label}' must contain at least one ingredient.");

                if (meal.Ingredients.Count > 50)
                    return ApiError($"Meal '{meal.Label}' exceeds the maximum of 50 ingredients.");

                foreach (var ing in meal.Ingredients)
                {
                    if (string.IsNullOrWhiteSpace(ing.Name))
                        return ApiError($"Meal '{meal.Label}': ingredient name cannot be empty.");

                    if (ing.QuantityGrams.HasValue && ing.QuantityGrams.Value <= 0)
                        return ApiError($"Meal '{meal.Label}': quantityGrams for '{ing.Name}' must be positive.");

                    if (ing.QuantityGrams.HasValue && ing.QuantityGrams.Value > 5000)
                        return ApiError($"Meal '{meal.Label}': quantityGrams for '{ing.Name}' exceeds 5000g. Check your units.");
                }
            }

            // ── Calculate ────────────────────────────────────────────────────────

            var result = await _nickelLoad.CalculateDailyLoadAsync(request, ct);
            sw.Stop();

            // ── Build Phase 2 safety metadata ────────────────────────────────────

            var alerts = new List<SafetyAlertDto>();

            // FATAL: high proportion of unmatched ingredients undermines the total
            int totalIngredients = result.TotalIngredients;
            int unmatched = result.UnmatchedIngredients.Count;
            if (totalIngredients > 0)
            {
                decimal unmatchedPct = (decimal)unmatched / totalIngredients * 100;
                if (unmatchedPct >= 30)
                {
                    alerts.Add(new SafetyAlertDto
                    {
                        Severity = "FATAL",
                        Code = "HIGH_UNMATCHED_RATE",
                        Message = $"{unmatched} of {totalIngredients} ingredients ({unmatchedPct:F0}%) could not be matched " +
                                  "to the nickel database. The reported total nickel load is INCOMPLETE and may be " +
                                  "significantly lower than actual. Do not use this result for clinical decisions " +
                                  "without resolving unmatched ingredients."
                    });
                }
                else if (unmatchedPct > 0)
                {
                    alerts.Add(new SafetyAlertDto
                    {
                        Severity = "WARNING",
                        Code = "UNMATCHED_INGREDIENTS",
                        Message = $"{unmatched} ingredient(s) could not be matched to the nickel database: " +
                                  string.Join(", ", result.UnmatchedIngredients.Take(10)) +
                                  ". Their nickel contribution is excluded from the total — actual load may be higher."
                    });
                }
            }

            // WARNING: EXCEEDED threshold
            if (result.RiskLevel == "EXCEEDED")
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code = "THRESHOLD_EXCEEDED",
                    Message = $"Daily nickel intake of {result.TotalNickelMcg} μg exceeds the " +
                              $"clinical threshold of {NickelThresholds.DailyThresholdMcg} μg/day " +
                              $"({result.ThresholdPercent}% of threshold). " +
                              "Individuals with systemic nickel allergy syndrome (SNAS) may experience flare reactions. " +
                              "Advise reducing consumption of the top-contributing ingredients identified in this response."
                });
            }
            else if (result.RiskLevel == "MODERATE")
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "APPROACHING_THRESHOLD",
                    Message = $"Daily nickel intake is {result.ThresholdPercent}% of the 150 μg/day threshold. " +
                              $"Remaining budget: {result.RemainingBudgetMcg} μg. " +
                              "Monitor cumulative intake across all meals."
                });
            }

            // WARNING: high-variability ingredients in the top contributors
            var highVariabilityIngredients = result.TopContributors
                .Where(i => i.HighVariability && i.NickelContributionMcg > 0)
                .OrderByDescending(i => i.NickelContributionMcg)
                .Take(5)
                .ToList();

            foreach (var hv in highVariabilityIngredients)
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code = "HIGH_VARIABILITY",
                    Message = $"'{hv.Name}' shows high nickel variability across samples. " +
                              $"Reported contribution of {hv.NickelContributionMcg} μg may vary significantly. " +
                              "Use with caution in low-nickel diet planning.",
                    AffectedIngredient = hv.Name
                });
            }

            // INFO: ingredients with preparation notes
            var prepNoteIngredients = result.TopContributors
                .Where(i => !string.IsNullOrEmpty(i.PreparationNote) && i.NickelContributionMcg > 0)
                .OrderByDescending(i => i.NickelContributionMcg)
                .Take(3)
                .ToList();

            foreach (var pn in prepNoteIngredients)
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "PREPARATION_NOTE",
                    Message = $"'{pn.Name}': {pn.PreparationNote}",
                    AffectedIngredient = pn.Name
                });
            }

            // INFO: no quantity data — all assumed serving sizes
            bool anyWithoutQuantity = request.Meals
                .SelectMany(m => m.Ingredients)
                .Any(i => !i.QuantityGrams.HasValue);

            if (anyWithoutQuantity)
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "ASSUMED_SERVING_SIZES",
                    Message = "One or more ingredients were submitted without a quantity. " +
                              "One reference serving size was assumed for each. " +
                              "For clinical accuracy, supply actual consumed quantities in grams."
                });
            }

            // Confidence score: penalise unmatched, variability, and low data points
            bool hasFatal = alerts.Any(a => a.Severity == "FATAL");
            bool hasWarnings = alerts.Any(a => a.Severity == "WARNING");

            int matchedPct = totalIngredients > 0
                ? (result.MatchedIngredients * 100 / totalIngredients)
                : 0;

            int baseScore = matchedPct;
            if (hasWarnings) baseScore = Math.Max(baseScore - 20, 0);
            if (hasFatal) baseScore = Math.Max(baseScore - 40, 0);

            // Boost for high data-quality ingredients
            var lowConfidenceContributors = result.TopContributors
                .Where(i => i.DataConfidence is "Low" or "Unknown")
                .Count();
            if (lowConfidenceContributors > result.TopContributors.Count / 2)
                baseScore = Math.Max(baseScore - 15, 0);

            baseScore = Math.Clamp(baseScore, 0, 100);
            string confidenceLevel = baseScore >= 70 ? "HIGH" : baseScore >= 40 ? "MEDIUM" : "LOW";

            var safetyMeta = new SafetyMeta
            {
                Disclaimer =
                    "Nickel content estimates are derived from Rebelytics R&D Inc. Summary Tables v9.1.1 (September 2025), " +
                    "compiled from 257 international food composition studies and 20,575 individual measurements. " +
                    "Individual food samples vary significantly; actual nickel content may differ. " +
                    "This calculation is for informational purposes only and does not constitute medical or dietary advice. " +
                    "Always consult a qualified healthcare professional before making dietary decisions based on nickel sensitivity. " +
                    "Recipzee / Platerly Inc. accepts no liability for adverse reactions arising from reliance on this data.",
                Confidence = new ConfidenceInfo
                {
                    Level = confidenceLevel,
                    Score = baseScore,
                    Explanation = $"{result.MatchedIngredients}/{result.TotalIngredients} ingredients matched " +
                                  $"({matchedPct}%); {lowConfidenceContributors} contributors have low data confidence." +
                                  (hasWarnings ? " Variability or unmatched-ingredient warnings present." : "") +
                                  $" Composite score: {baseScore}/100."
                },
                DataFreshness = new DataFreshnessInfo
                {
                    SourceVersion = "Rebelytics R&D Inc. v9.1.1",
                    SourceYear = 2025,
                    SourceCitation =
                        "Rebelytics R&D Inc., \"Nickel in Foods: Summary Tables, Unweighted, September 2025\". " +
                        "Compiled from 257 international food composition studies, 20,575 individual measurements " +
                        "across 826 unique foods. Available: rebelytics.ca/LND/lowNiDiet_r9.1.1_summaryTables.pdf",
                    IsStale = false,
                    LastImportedAt = _config["B2BApi:NickelDataLastImportedAt"] ?? "2026-03-29"
                },
                Alerts = alerts,
                GeneratedAt = DateTime.UtcNow
            };

            var response = new SafetyApiResponse<NickelDailyLoadResponse>
            {
                Data = result,
                Safety = safetyMeta
            };

            // ── Audit log ────────────────────────────────────────────────────────

            var apiKeyId = GetApiKeyId();
            var highestSeverity = hasFatal ? "FATAL" : hasWarnings ? "WARNING"
                : alerts.Any(a => a.Severity == "INFO") ? "INFO" : null;

            var ingredientContext = string.Join(", ",
                request.Meals
                    .SelectMany(m => m.Ingredients.Select(i => i.Name))
                    .Distinct()
                    .Take(20));

            _ = _audit.LogAsync(new SafetyAuditLog
            {
                HttpMethod = "POST",
                Endpoint = "/api/b2b/nickel/daily-load",
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
                ConfidenceLevel = confidenceLevel
            }, CancellationToken.None);

            return Ok(response);
        }
    }
}
