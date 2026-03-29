namespace Platerly.Api.DTOs
{
    /// <summary>
    /// Standard safety-layer envelope wrapping every response from allergen, substitution,
    /// and nickel-safety endpoints. Every field is mandatory — nothing is nullable.
    /// </summary>
    public class SafetyApiResponse<T>
    {
        /// <summary>The actual payload (substitutions, alerts, etc.).</summary>
        public T Data { get; set; } = default!;

        /// <summary>Safety metadata — always present on every response.</summary>
        public SafetyMeta Safety { get; set; } = default!;
    }

    /// <summary>
    /// The safety metadata block injected on every safety-layer response.
    /// </summary>
    public class SafetyMeta
    {
        /// <summary>Item 2 — mandatory disclaimer, always injected.</summary>
        public string Disclaimer { get; set; } = string.Empty;

        /// <summary>Item 1 — confidence score for the data in this response.</summary>
        public ConfidenceInfo Confidence { get; set; } = default!;

        /// <summary>Item 3 — data freshness and source provenance.</summary>
        public DataFreshnessInfo DataFreshness { get; set; } = default!;

        /// <summary>Items 4 &amp; 5 — out-of-range and tiered safety alerts (FATAL / WARNING / INFO).</summary>
        public List<SafetyAlertDto> Alerts { get; set; } = new();

        /// <summary>UTC timestamp this response was generated.</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    // ────────────────────────────────────────────────────────────────────
    // Item 1 — Confidence Scoring
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Confidence score for the data included in this response.</summary>
    public class ConfidenceInfo
    {
        /// <summary>"HIGH" | "MEDIUM" | "LOW" | "UNKNOWN"</summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>Numeric score 0–100 derived from data quality indicators.</summary>
        public int Score { get; set; }

        /// <summary>Human-readable explanation of the score.</summary>
        public string Explanation { get; set; } = string.Empty;
    }

    // ────────────────────────────────────────────────────────────────────
    // Item 3 — Data Freshness
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Provenance and freshness of the underlying dataset.</summary>
    public class DataFreshnessInfo
    {
        /// <summary>Dataset version identifier (e.g. "Rebelytics v9.1.1").</summary>
        public string SourceVersion { get; set; } = string.Empty;

        /// <summary>Year the source data was published.</summary>
        public int SourceYear { get; set; }

        /// <summary>Full citation for the primary data source.</summary>
        public string SourceCitation { get; set; } = string.Empty;

        /// <summary>True when source data is older than 2 years.</summary>
        public bool IsStale { get; set; }

        /// <summary>ISO-8601 date of the last seeder/import run on this server.</summary>
        public string LastImportedAt { get; set; } = string.Empty;
    }

    // ────────────────────────────────────────────────────────────────────
    // Items 4 & 5 — Out-of-Range Alerts + Three-Tier Error Hierarchy
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A single safety alert.
    /// Severity tiers:
    ///   FATAL   — data is unreliable or contradictory; do not act on it without verification.
    ///   WARNING — data should be used with caution (high variability, low confidence, etc.).
    ///   INFO    — informational note; data is usable but context is helpful.
    /// </summary>
    public class SafetyAlertDto
    {
        /// <summary>"FATAL" | "WARNING" | "INFO"</summary>
        public string Severity { get; set; } = string.Empty;

        /// <summary>
        /// Machine-readable alert code.
        /// Examples: NICKEL_VALUE_IMPLAUSIBLE, CLASSIFICATION_CONTRADICTION,
        ///           HIGH_VARIABILITY, LOW_CONFIDENCE, LIMITED_DATA_POINTS,
        ///           DATA_STALE, NO_NICKEL_DATA.
        /// </summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Human-readable description of the alert.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Ingredient or field that triggered this alert, if applicable.</summary>
        public string? AffectedIngredient { get; set; }
    }
}
