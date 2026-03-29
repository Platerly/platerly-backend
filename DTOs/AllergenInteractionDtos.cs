namespace Platerly.Api.DTOs
{
    // ── Request ────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/b2b/allergens/interaction-analysis
    /// Analyses a multi-allergen patient profile for cross-reactive allergen pairs,
    /// calculates a compound systemic risk score, and returns a combined safe food window.
    /// </summary>
    public class AllergenInteractionRequest
    {
        /// <summary>
        /// The patient's confirmed allergen list — allergen type strings
        /// (e.g., "Peanuts", "Tree Nuts", "Milk", "Sesame").
        /// Minimum 2 allergens required for interaction analysis.
        /// </summary>
        public List<string> Allergens { get; set; } = new();

        /// <summary>
        /// Whether to include the combined safe food window in the response.
        /// Default true. Set false to skip the DB query for faster responses.
        /// </summary>
        public bool IncludeSafeFoods { get; set; } = true;

        /// <summary>
        /// Maximum safe foods to return. Default 30, max 100.
        /// </summary>
        public int SafeFoodLimit { get; set; } = 30;
    }

    // ── Response ───────────────────────────────────────────────────────────────

    public class AllergenInteractionResult
    {
        // ── Submitted profile ─────────────────────────────────────────────────

        /// <summary>Allergens that were recognised and analysed.</summary>
        public List<string> RecognisedAllergens { get; set; } = new();

        /// <summary>Allergens submitted that are not in the knowledge base.</summary>
        public List<string> UnknownAllergens { get; set; } = new();

        // ── Cross-reactivity map ──────────────────────────────────────────────

        /// <summary>
        /// All cross-reactive pairs detected within the submitted allergen profile.
        /// </summary>
        public List<CrossReactivePair> CrossReactivePairs { get; set; } = new();

        /// <summary>
        /// Allergens not involved in any cross-reactive interaction
        /// (managed independently without compounding risk).
        /// </summary>
        public List<string> IsolatedAllergens { get; set; } = new();

        // ── Compound risk assessment ──────────────────────────────────────────

        /// <summary>
        /// Compound risk score 0–100 accounting for allergen count,
        /// cross-reactive pairs, and anaphylaxis-prone allergens.
        /// </summary>
        public int CompoundRiskScore { get; set; }

        /// <summary>
        /// Risk classification:
        ///   STANDARD  — 0–25 (routine multi-allergen management)
        ///   ELEVATED  — 26–50 (diligent label reading required)
        ///   HIGH      — 51–75 (significant dietary restriction; dietitian referral)
        ///   SEVERE    — 76–100 (high systemic risk; specialist management; carry epinephrine)
        /// </summary>
        public string RiskClassification { get; set; } = string.Empty;

        /// <summary>Explanation of how the compound score was computed.</summary>
        public string RiskScoreExplanation { get; set; } = string.Empty;

        // ── Clinical recommendations ──────────────────────────────────────────

        /// <summary>Per-pair clinical management recommendations.</summary>
        public List<string> ClinicalRecommendations { get; set; } = new();

        // ── Combined safe food window ─────────────────────────────────────────

        /// <summary>
        /// Foods verified safe against ALL of the patient's allergens simultaneously.
        /// Null when IncludeSafeFoods = false.
        /// </summary>
        public List<SafeFoodEntry>? CombinedSafeFoods { get; set; }

        /// <summary>
        /// When cross-reactive allergens expand the effective restriction,
        /// this lists the additional allergen types that were also excluded
        /// from the safe food window beyond what was explicitly submitted.
        /// </summary>
        public List<string> EffectiveAllergenFilter { get; set; } = new();
    }

    /// <summary>
    /// A detected cross-reactive allergen pair within the patient's profile.
    /// </summary>
    public class CrossReactivePair
    {
        public string AllergenA { get; set; } = string.Empty;
        public string AllergenB { get; set; } = string.Empty;

        /// <summary>
        /// The molecular or immunological mechanism underlying the cross-reactivity.
        /// Example: "Shared tropomyosin protein", "2S albumin family homology".
        /// </summary>
        public string Mechanism { get; set; } = string.Empty;

        /// <summary>
        /// Estimated co-sensitisation likelihood in the allergic population (0–100%).
        /// </summary>
        public int CoSensitisationLikelihoodPercent { get; set; }

        /// <summary>"HIGH" | "MODERATE" | "LOW"</summary>
        public string ClinicalSignificance { get; set; } = string.Empty;

        /// <summary>Clinical note explaining the practical implication for this pair.</summary>
        public string ClinicalNote { get; set; } = string.Empty;

        /// <summary>Score contribution this pair adds to the compound risk score.</summary>
        public int RiskContribution { get; set; }
    }

    /// <summary>
    /// A food item verified safe against the full compound allergen profile.
    /// </summary>
    public class SafeFoodEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? NutritionalCategory { get; set; }
        public bool IsVerified { get; set; }
    }
}
