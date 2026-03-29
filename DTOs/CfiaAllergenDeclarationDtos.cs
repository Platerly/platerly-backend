namespace Platerly.Api.DTOs
{
    // ── Request ────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/b2b/compliance/cfia-allergen-declaration
    /// Accepts a list of ingredient names and returns bilingual CFIA-compliant
    /// allergen declaration text ready for use on Canadian food packaging.
    /// </summary>
    public class CfiaAllergenDeclarationRequest
    {
        /// <summary>
        /// List of ingredient names as they appear in the recipe or product formula.
        /// Names are matched against the allergen database for structured detection,
        /// with a keyword-lexicon fallback for unrecognised ingredients.
        /// </summary>
        public List<string> Ingredients { get; set; } = new();

        /// <summary>
        /// Optional product or recipe name used only for labelling context in the response.
        /// </summary>
        public string? ProductName { get; set; }
    }

    // ── Response ───────────────────────────────────────────────────────────────

    public class CfiaAllergenDeclarationResult
    {
        // ── Ready-to-paste label copy ────────────────────────────────────────

        /// <summary>
        /// Bilingual "Contains" statement. Null when no priority allergens are detected.
        /// </summary>
        public BilingualText? ContainsDeclaration { get; set; }

        /// <summary>
        /// Bilingual "May contain" statement. Null when no cross-contact allergens are detected.
        /// </summary>
        public BilingualText? MayContainDeclaration { get; set; }

        /// <summary>
        /// Both statements concatenated in a single bilingual block —
        /// ready to copy-paste onto packaging copy.
        /// Null when neither statement has content.
        /// </summary>
        public BilingualText? CombinedLabel { get; set; }

        // ── Allergen summary ─────────────────────────────────────────────────

        /// <summary>
        /// All CFIA priority allergens detected in the "Contains" category.
        /// </summary>
        public List<CfiaAllergenEntry> ContainsAllergens { get; set; } = new();

        /// <summary>
        /// All CFIA priority allergens detected in the "May Contain" category
        /// (cross-contact or may-contain flags on individual ingredients).
        /// </summary>
        public List<CfiaAllergenEntry> MayContainAllergens { get; set; } = new();

        // ── Per-ingredient breakdown ─────────────────────────────────────────

        /// <summary>
        /// Per-ingredient allergen breakdown showing which allergens each ingredient contributed.
        /// </summary>
        public List<CfiaIngredientBreakdown> IngredientBreakdown { get; set; } = new();

        // ── Coverage ─────────────────────────────────────────────────────────

        /// <summary>
        /// Ingredients not matched in the allergen database (neither direct nor keyword match).
        /// Their allergen content is unknown — see Safety alerts.
        /// </summary>
        public List<string> UnmatchedIngredients { get; set; } = new();

        /// <summary>Total ingredients submitted.</summary>
        public int TotalIngredients { get; set; }

        /// <summary>Ingredients for which at least partial allergen data was found.</summary>
        public int MatchedIngredients { get; set; }

        /// <summary>
        /// Detection method summary: "structured" (GlobalIngredientAllergen table),
        /// "keyword" (AllergenKeyword lexicon), or "unmatched".
        /// </summary>
        public DetectionSummary DetectionSummary { get; set; } = new();
    }

    /// <summary>
    /// A single CFIA priority allergen with bilingual label copy.
    /// </summary>
    public class CfiaAllergenEntry
    {
        /// <summary>Internal allergen type key (matches GlobalIngredientAllergen.AllergenType).</summary>
        public string AllergenType { get; set; } = string.Empty;

        /// <summary>CFIA English allergen name (e.g., "Wheat and triticale").</summary>
        public string EnglishName { get; set; } = string.Empty;

        /// <summary>CFIA French allergen name (e.g., "Blé et triticale").</summary>
        public string FrenchName { get; set; } = string.Empty;

        /// <summary>Ingredients that contributed this allergen detection.</summary>
        public List<string> SourceIngredients { get; set; } = new();
    }

    /// <summary>
    /// Allergen contribution from a single ingredient.
    /// </summary>
    public class CfiaIngredientBreakdown
    {
        public string IngredientName { get; set; } = string.Empty;

        /// <summary>
        /// "structured" — matched via GlobalIngredientAllergen table.
        /// "keyword"    — matched via AllergenKeyword lexicon.
        /// "unmatched"  — not found in either source.
        /// </summary>
        public string DetectionMethod { get; set; } = string.Empty;

        /// <summary>Allergen types detected as definite "Contains" for this ingredient.</summary>
        public List<string> ContainsAllergens { get; set; } = new();

        /// <summary>Allergen types detected as "May Contain" / cross-contact for this ingredient.</summary>
        public List<string> MayContainAllergens { get; set; } = new();
    }

    public class BilingualText
    {
        public string English { get; set; } = string.Empty;
        public string French { get; set; } = string.Empty;
    }

    public class DetectionSummary
    {
        /// <summary>Ingredients matched via structured GlobalIngredientAllergen data.</summary>
        public int StructuredMatches { get; set; }

        /// <summary>Ingredients matched via keyword-lexicon fallback only.</summary>
        public int KeywordMatches { get; set; }

        /// <summary>Ingredients not matched by either method.</summary>
        public int UnmatchedCount { get; set; }
    }
}
