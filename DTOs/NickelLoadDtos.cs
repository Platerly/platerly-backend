namespace Platerly.Api.DTOs
{
    // ── Constants (Mislankar & Zirwas 2013) ────────────────────────────────────

    /// <summary>
    /// Clinical nickel thresholds from Mislankar &amp; Zirwas 2013.
    /// Cited: Mislankar M, Zirwas MJ. "Low-nickel diet scoring system for
    /// systemic nickel allergy." Cutis. 2013;92(1):40-44.
    /// </summary>
    public static class NickelThresholds
    {
        /// <summary>Daily nickel intake threshold in micrograms (Mislankar & Zirwas 2013).</summary>
        public const decimal DailyThresholdMcg = 150m;

        /// <summary>Below this percentage of threshold: SAFE.</summary>
        public const decimal SafePercent = 50m;

        /// <summary>Above this percentage of threshold: EXCEEDED.</summary>
        public const decimal ExceededPercent = 100m;

        public const string Citation =
            "Mislankar M, Zirwas MJ. \"Low-nickel diet scoring system for systemic nickel allergy.\" " +
            "Cutis. 2013;92(1):40-44. PMID: 23977692. Daily oral nickel threshold: 150 μg/day.";
    }

    // ── Request ────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/b2b/nickel/daily-load
    /// </summary>
    public class NickelDailyLoadRequest
    {
        /// <summary>
        /// The full day's meals. At least one meal required, minimum one ingredient per meal.
        /// </summary>
        public List<NickelMealInput> Meals { get; set; } = new();
    }

    public class NickelMealInput
    {
        /// <summary>Meal label for the response (e.g., "Breakfast", "Lunch"). Required.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Optional caller-supplied recipe identifier for this meal.
        /// Included in match-failure log entries to help identify which recipes
        /// have the most unresolved ingredient names.
        /// </summary>
        public string? RecipeId { get; set; }

        /// <summary>Ingredients consumed in this meal.</summary>
        public List<NickelIngredientInput> Ingredients { get; set; } = new();
    }

    public class NickelIngredientInput
    {
        /// <summary>Ingredient name as it would appear in a recipe (used for DB lookup).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Consumed quantity in grams. When provided, nickel is scaled proportionally
        /// from the reference serving size. When omitted, one reference serving is assumed.
        /// </summary>
        public decimal? QuantityGrams { get; set; }
    }

    // ── Response ───────────────────────────────────────────────────────────────

    public class NickelDailyLoadResponse
    {
        // ── Totals ────────────────────────────────────────────────────────────

        /// <summary>Total estimated nickel intake for the day in micrograms (μg).</summary>
        public decimal TotalNickelMcg { get; set; }

        /// <summary>Percentage of the 150 μg/day clinical threshold consumed.</summary>
        public decimal ThresholdPercent { get; set; }

        /// <summary>Risk assessment: "SAFE", "MODERATE", or "EXCEEDED".</summary>
        public string RiskLevel { get; set; } = string.Empty;

        /// <summary>
        /// Remaining daily nickel budget in μg.
        /// Negative value means the threshold has been exceeded.
        /// </summary>
        public decimal RemainingBudgetMcg { get; set; }

        // ── Clinical reference ────────────────────────────────────────────────

        /// <summary>The daily threshold used for comparison.</summary>
        public decimal DailyThresholdMcg { get; set; } = NickelThresholds.DailyThresholdMcg;

        /// <summary>Citation for the clinical threshold.</summary>
        public string ThresholdCitation { get; set; } = NickelThresholds.Citation;

        // ── Breakdowns ────────────────────────────────────────────────────────

        /// <summary>Per-meal nickel contribution.</summary>
        public List<NickelMealResult> Meals { get; set; } = new();

        /// <summary>
        /// All ingredients ranked by nickel contribution, highest first.
        /// This is the flat cross-meal list for identifying top contributors.
        /// </summary>
        public List<NickelIngredientResult> TopContributors { get; set; } = new();

        // ── Coverage ──────────────────────────────────────────────────────────

        /// <summary>Number of ingredients for which nickel data was found.</summary>
        public int MatchedIngredients { get; set; }

        /// <summary>Total number of ingredients submitted.</summary>
        public int TotalIngredients { get; set; }

        /// <summary>
        /// Ingredients that could not be matched to the nickel database.
        /// Their nickel contribution is NOT included in TotalNickelMcg — this means
        /// the actual load may be higher than reported.
        /// </summary>
        public List<string> UnmatchedIngredients { get; set; } = new();
    }

    public class NickelMealResult
    {
        public string Label { get; set; } = string.Empty;
        public decimal NickelMcg { get; set; }
        public decimal ThresholdPercent { get; set; }
        public List<NickelIngredientResult> Ingredients { get; set; } = new();
    }

    public class NickelIngredientResult
    {
        public string Name { get; set; } = string.Empty;
        public string MealLabel { get; set; } = string.Empty;

        /// <summary>Submitted quantity in grams (null if not provided).</summary>
        public decimal? QuantityGrams { get; set; }

        /// <summary>Reference serving size used for scaling.</summary>
        public int? ReferenceServingSizeGrams { get; set; }

        /// <summary>Nickel per reference serving from the database.</summary>
        public decimal? NickelPerServingMcg { get; set; }

        /// <summary>Scaled nickel contribution for the actual quantity submitted.</summary>
        public decimal NickelContributionMcg { get; set; }

        /// <summary>Percentage of daily threshold this single ingredient represents.</summary>
        public decimal ContributionPercent { get; set; }

        public string NickelClassification { get; set; } = "Unknown";
        public string DataConfidence { get; set; } = "Unknown";

        /// <summary>True when standard deviation is high relative to the mean.</summary>
        public bool HighVariability { get; set; }

        /// <summary>Preparation note that may affect nickel content (e.g., "draining reduces nickel by ~30%").</summary>
        public string? PreparationNote { get; set; }

        /// <summary>True when nickel data was found for this ingredient.</summary>
        public bool Matched { get; set; }
    }
}
