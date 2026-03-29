namespace Platerly.Api.DTOs
{
    // ── Search ──────────────────────────────────────────────────────────────────

    public class IngredientSearchResult
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>URL-safe slug derived from NormalizedName (spaces → hyphens).</summary>
        public string Slug { get; set; } = string.Empty;

        /// <summary>"Low", "Moderate", "High", or "Unknown"</summary>
        public string NickelClassification { get; set; } = "Unknown";

        public string? NutritionalCategory { get; set; }
        public bool IsVerified { get; set; }
    }

    // ── Detail ──────────────────────────────────────────────────────────────────

    public class IngredientDetailResult
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? NutritionalCategory { get; set; }
        public List<string> DietaryFlags { get; set; } = new();
        public bool IsVerified { get; set; }

        // ── Nickel ──────────────────────────────────────────────────────────────

        /// <summary>"Low", "Moderate", "High", or "Unknown"</summary>
        public string NickelClassification { get; set; } = "Unknown";

        /// <summary>
        /// Plain-English explanation of what this classification means for
        /// someone managing nickel allergy.
        /// </summary>
        public string NickelSummary { get; set; } = string.Empty;

        /// <summary>Mean nickel content in μg per serving. Null if no data.</summary>
        public decimal? NickelMicrogramsPerServing { get; set; }

        /// <summary>Serving size in grams used for the measurement.</summary>
        public int? NickelServingSizeGrams { get; set; }

        /// <summary>"High", "Medium", "Low", or "Unknown"</summary>
        public string NickelConfidenceLevel { get; set; } = "Unknown";

        /// <summary>Number of individual measurements behind the mean.</summary>
        public int? NickelDataPointCount { get; set; }

        /// <summary>
        /// True when the standard deviation is exceptionally high relative to the mean,
        /// indicating wide variability between samples.
        /// </summary>
        public bool NickelHighVariability { get; set; }

        /// <summary>Optional preparation note affecting nickel content.</summary>
        public string? NickelPreparationNote { get; set; }

        // ── Allergens ───────────────────────────────────────────────────────────

        /// <summary>Allergen types present in this ingredient.</summary>
        public List<string> AllergenTypes { get; set; } = new();

        // ── Cross-reactivity ────────────────────────────────────────────────────

        /// <summary>
        /// Cross-reactivity pairs involving this ingredient's allergen types.
        /// Empty list when no allergens are present or no cross-reactivity data exists.
        /// </summary>
        public List<IngredientCrossReactivityEntry> CrossReactivity { get; set; } = new();

        // ── Source ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Full data source citation shown on the ingredient page.
        /// </summary>
        public string DataSourceCitation { get; set; } =
            "Nickel data sourced from Rebelytics R&D Inc., Nickel in Foods Summary Tables v9.1.1, " +
            "September 2025. Compiled from 257 international food composition studies, " +
            "20,575 individual measurements.";
    }

    public class IngredientCrossReactivityEntry
    {
        /// <summary>The other allergen that cross-reacts with this ingredient's allergen.</summary>
        public string RelatedAllergen { get; set; } = string.Empty;

        /// <summary>The ingredient's allergen that triggers this cross-reactivity.</summary>
        public string SourceAllergen { get; set; } = string.Empty;

        /// <summary>Immunological mechanism (e.g., "IgE-mediated cross-reactivity").</summary>
        public string Mechanism { get; set; } = string.Empty;

        /// <summary>Estimated likelihood of co-sensitisation in percent.</summary>
        public int CoSensitisationLikelihoodPercent { get; set; }

        /// <summary>"HIGH", "MODERATE", or "LOW"</summary>
        public string ClinicalSignificance { get; set; } = string.Empty;

        /// <summary>Plain-English clinical note about the cross-reaction.</summary>
        public string ClinicalNote { get; set; } = string.Empty;
    }
}
