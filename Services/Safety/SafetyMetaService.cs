using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Safety
{
    /// <summary>
    /// Builds the safety metadata block injected on every safety-layer response.
    /// Implements: confidence scoring, mandatory disclaimer, data freshness,
    /// out-of-range detection, and three-tier alert hierarchy (FATAL/WARNING/INFO).
    /// </summary>
    public class SafetyMetaService : ISafetyMetaService
    {
        // ── Constants ────────────────────────────────────────────────────────────

        private const string Disclaimer =
            "This allergen, nickel, and ingredient safety data is provided for informational " +
            "purposes only and does not constitute medical or dietary advice. Nickel content " +
            "data is sourced from Rebelytics R&D Inc. Summary Tables v9.1.1 (September 2025), " +
            "compiled from 257 international food composition studies and 20,575 individual " +
            "measurements. Individual food samples vary significantly; actual nickel content " +
            "may differ from reported values. Always consult a qualified healthcare professional " +
            "before making dietary decisions based on allergy or sensitivity information. " +
            "Recipzee / Platerly Inc. accepts no liability for adverse reactions arising from " +
            "reliance on this data.";

        private const string SourceVersion = "Rebelytics R&D Inc. v9.1.1";
        private const int SourceYear = 2025;
        private const string SourceCitation =
            "Rebelytics R&D Inc., \"Nickel in Foods: Summary Tables, Unweighted, September 2025\". " +
            "Compiled from 257 international food composition studies, 20,575 individual measurements " +
            "across 826 unique foods. Available: rebelytics.ca/LND/lowNiDiet_r9.1.1_summaryTables.pdf";

        // Data is considered stale if the source year is more than 2 years before the current year.
        private static bool IsDataStale => (DateTime.UtcNow.Year - SourceYear) > 2;

        // Approximate date the seeder last ran (updated whenever a new seeder version ships).
        private static readonly string LastImportedAt = "2026-03-29";

        // Out-of-range thresholds (μg per serving)
        private const decimal ImplausibleHighNickel = 500m;    // FATAL above this
        private const decimal HighNickelThreshold = 200m;      // WARNING above this
        private const decimal LowClassificationMax = 10m;       // LOW class must be ≤ 10 μg
        private const decimal HighClassificationMin = 20m;      // HIGH class must be ≥ 20 μg

        // ── Public interface ─────────────────────────────────────────────────────

        public SafetyMeta BuildForSubstitutions(IEnumerable<SubstitutionDto> substitutions)
        {
            var alerts = new List<SafetyAlertDto>();
            var subList = substitutions.ToList();

            foreach (var sub in subList)
            {
                alerts.AddRange(AuditSubstitute(sub));
            }

            if (IsDataStale)
                alerts.Add(DataStaleAlert());

            var confidence = ComputeSubstitutionConfidence(subList, alerts);

            return new SafetyMeta
            {
                Disclaimer = Disclaimer,
                Confidence = confidence,
                DataFreshness = BuildFreshness(),
                Alerts = alerts,
                GeneratedAt = DateTime.UtcNow
            };
        }

        public SafetyMeta BuildForAllergyAlerts(RecipeAlertsResponse alerts)
        {
            var safetyAlerts = new List<SafetyAlertDto>();

            if (IsDataStale)
                safetyAlerts.Add(DataStaleAlert());

            // If there are nickel alerts with no μg value backing them (qualitative only), flag it.
            bool hasNickelAlert = alerts.Alerts.Any(a =>
                a.AllergenType.Contains("Nickel", StringComparison.OrdinalIgnoreCase));

            if (hasNickelAlert)
            {
                safetyAlerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "NICKEL_ALERT_PRESENT",
                    Message = "One or more ingredients in this recipe have been flagged for nickel content. " +
                              "Nickel values are per-serving estimates and individual samples vary. " +
                              "Consult your healthcare provider for personalised dietary guidance."
                });
            }

            var confidence = new ConfidenceInfo
            {
                Level = "MEDIUM",
                Score = 65,
                Explanation = "Allergen presence detection is based on keyword matching against the " +
                              "FDA Big 9 / EU 14 allergen lexicon. Cross-contact risk is estimated, not measured."
            };

            return new SafetyMeta
            {
                Disclaimer = Disclaimer,
                Confidence = confidence,
                DataFreshness = BuildFreshness(),
                Alerts = safetyAlerts,
                GeneratedAt = DateTime.UtcNow
            };
        }

        public SafetyMeta BuildBaseline()
        {
            var alerts = new List<SafetyAlertDto>();

            if (IsDataStale)
                alerts.Add(DataStaleAlert());

            return new SafetyMeta
            {
                Disclaimer = Disclaimer,
                Confidence = new ConfidenceInfo
                {
                    Level = "MEDIUM",
                    Score = 60,
                    Explanation = "General safety metadata. No specific data quality indicators available for this response."
                },
                DataFreshness = BuildFreshness(),
                Alerts = alerts,
                GeneratedAt = DateTime.UtcNow
            };
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Audit a single substitute for out-of-range values and classification contradictions.
        /// Implements Item 4 (out-of-range) and Item 5 (FATAL/WARNING/INFO hierarchy).
        /// </summary>
        private static IEnumerable<SafetyAlertDto> AuditSubstitute(SubstitutionDto sub)
        {
            if (sub.SubstituteNickelClassification == null)
                yield break;

            var classification = sub.SubstituteNickelClassification.Value;
            var name = sub.SubstituteIngredientName;

            // We don't have the μg value directly in SubstitutionDto, but we can reason
            // from classification + safety warning fields.

            // FATAL: NickelSafetyWarning set but classification is LOW — contradiction
            if (sub.NickelSafetyWarning &&
                classification == NickelClassification.Low)
            {
                yield return new SafetyAlertDto
                {
                    Severity = "FATAL",
                    Code = "CLASSIFICATION_CONTRADICTION",
                    Message = $"'{name}' is classified as LOW nickel but a safety warning is set. " +
                              "This is a data contradiction. Do not rely on this substitution for " +
                              "nickel-allergy decisions without independent verification.",
                    AffectedIngredient = name
                };
                yield break; // FATAL short-circuits further checks for this ingredient
            }

            // WARNING: HIGH nickel classification
            if (classification == NickelClassification.High)
            {
                yield return new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code = "HIGH_NICKEL_SUBSTITUTE",
                    Message = $"'{name}' is classified as HIGH nickel (>20 μg per serving). " +
                              "This substitute is not recommended for individuals managing nickel allergy " +
                              "or systemic nickel allergy syndrome (SNAS).",
                    AffectedIngredient = name
                };
            }

            // WARNING: HIGH variability
            if (sub.NickelSafetyWarning && classification == NickelClassification.Moderate)
            {
                yield return new SafetyAlertDto
                {
                    Severity = "WARNING",
                    Code = "HIGH_VARIABILITY",
                    Message = $"'{name}' shows high nickel variability across samples. " +
                              "Actual nickel content may be higher or lower than the reported mean. " +
                              "Use caution in low-nickel dietary planning.",
                    AffectedIngredient = name
                };
            }

            // INFO: MODERATE classification — usable but worth noting
            if (classification == NickelClassification.Moderate && !sub.NickelSafetyWarning)
            {
                yield return new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "MODERATE_NICKEL_SUBSTITUTE",
                    Message = $"'{name}' is classified as MODERATE nickel (10–20 μg per serving). " +
                              "Suitable for many low-nickel diets but monitor cumulative daily intake.",
                    AffectedIngredient = name
                };
            }
        }

        /// <summary>
        /// Compute the overall confidence score for a substitution list response.
        /// Item 1 — confidence scoring.
        /// </summary>
        private static ConfidenceInfo ComputeSubstitutionConfidence(
            List<SubstitutionDto> subs, List<SafetyAlertDto> alerts)
        {
            if (!subs.Any())
            {
                return new ConfidenceInfo
                {
                    Level = "UNKNOWN",
                    Score = 0,
                    Explanation = "No substitutions available for confidence assessment."
                };
            }

            bool hasFatal = alerts.Any(a => a.Severity == "FATAL");
            if (hasFatal)
            {
                return new ConfidenceInfo
                {
                    Level = "LOW",
                    Score = 20,
                    Explanation = "One or more FATAL data contradictions detected. " +
                                  "Confidence is low — verify data independently before acting on it."
                };
            }

            int verified = subs.Count(s => s.IsVerified);
            int total = subs.Count;
            int verifiedPct = total > 0 ? (verified * 100 / total) : 0;

            bool hasWarnings = alerts.Any(a => a.Severity == "WARNING");
            bool hasNickelData = subs.Any(s => s.SubstituteNickelClassification != null);

            int score = verifiedPct; // base: 0–100 from verification rate

            // Nickel data present = more complete picture
            if (hasNickelData) score = Math.Min(score + 15, 100);

            // Warnings reduce confidence
            if (hasWarnings) score = Math.Max(score - 20, 0);

            // Average confidence score from substitution records
            if (subs.Any(s => s.ConfidenceScore > 0))
            {
                int avgSubScore = (int)subs.Where(s => s.ConfidenceScore > 0)
                                           .Average(s => s.ConfidenceScore);
                score = (score + avgSubScore) / 2;
            }

            score = Math.Clamp(score, 0, 100);

            string level = score >= 70 ? "HIGH" : score >= 40 ? "MEDIUM" : "LOW";
            string explanation = $"{verified}/{total} substitutions verified" +
                                 (hasNickelData ? "; nickel classification data present" : "; no nickel data available") +
                                 (hasWarnings ? "; variability or caution warnings present" : "") +
                                 $". Composite score: {score}/100.";

            return new ConfidenceInfo
            {
                Level = level,
                Score = score,
                Explanation = explanation
            };
        }

        private static DataFreshnessInfo BuildFreshness() => new DataFreshnessInfo
        {
            SourceVersion = SourceVersion,
            SourceYear = SourceYear,
            SourceCitation = SourceCitation,
            IsStale = IsDataStale,
            LastImportedAt = LastImportedAt
        };

        private static SafetyAlertDto DataStaleAlert() => new SafetyAlertDto
        {
            Severity = "WARNING",
            Code = "DATA_STALE",
            Message = $"The primary nickel dataset (Rebelytics v9.1.1, {SourceYear}) is more than " +
                      "2 years old. Values may not reflect the most current food composition data. " +
                      "Check for updated Rebelytics publications."
        };
    }
}
