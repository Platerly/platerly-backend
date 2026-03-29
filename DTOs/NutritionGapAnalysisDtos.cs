namespace Platerly.Api.DTOs
{
    // ── Request ────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/b2b/nutrition/gap-analysis
    /// Identifies nutrient deficiency risks arising from a multi-allergen avoidance
    /// profile and returns compensating foods validated for allergen safety.
    ///
    /// When nickelSensitive is true, every compensating food is additionally
    /// screened against the Rebelytics v9.1.1 nickel database —
    /// only LOW-classified foods are returned.
    /// </summary>
    public class NutritionGapRequest
    {
        /// <summary>
        /// Confirmed allergens the patient avoids. Used to identify which food groups
        /// are excluded and what nutrients they provide.
        /// </summary>
        public List<string> AllergenProfile { get; set; } = new();

        /// <summary>
        /// When true, compensating food recommendations are filtered to ingredients
        /// with NickelClassification = Low (Rebelytics v9.1.1 data).
        /// Ingredients with no nickel data are flagged but included.
        /// Default: false.
        /// </summary>
        public bool NickelSensitive { get; set; } = false;

        /// <summary>
        /// Optional list of specific nutrients to analyse.
        /// When provided, only gaps for these nutrients are returned.
        /// Accepted values: Calcium, Vitamin D, Vitamin B12, Iron, Omega-3, Vitamin E,
        ///   Magnesium, Zinc, Selenium, Choline, Iodine, Riboflavin, Niacin,
        ///   Folate, Fiber, Protein, Thiamine, Healthy Fats.
        /// When omitted, all relevant gaps are returned.
        /// </summary>
        public List<string>? FocusNutrients { get; set; }

        /// <summary>
        /// Maximum compensating foods to return per nutrient gap. Default 5, max 20.
        /// </summary>
        public int MaxFoodsPerGap { get; set; } = 5;
    }

    // ── Response ───────────────────────────────────────────────────────────────

    public class NutritionGapResult
    {
        /// <summary>All identified nutrient gaps with compensating food recommendations.</summary>
        public List<NutrientGapEntry> NutrientGaps { get; set; } = new();

        /// <summary>
        /// Overall dietary risk classification across all identified gaps:
        ///   NONE     — no gaps identified
        ///   LOW      — only low-severity gaps
        ///   MODERATE — at least one moderate gap
        ///   HIGH     — at least one high-severity gap
        ///   CRITICAL — multiple high-severity gaps or compound escalation
        /// </summary>
        public string OverallRisk { get; set; } = "NONE";

        /// <summary>The highest-priority nutrients to address first.</summary>
        public List<string> PriorityNutrients { get; set; } = new();

        /// <summary>Total number of nutrient gaps identified.</summary>
        public int TotalGapsIdentified { get; set; }

        /// <summary>
        /// True when all compensating foods were additionally filtered for low nickel content.
        /// </summary>
        public bool NickelValidated { get; set; }

        /// <summary>
        /// Allergens submitted that are not in the gap-analysis knowledge base.
        /// Their dietary impact is not modelled — gaps may be underestimated.
        /// </summary>
        public List<string> UnmodelledAllergens { get; set; } = new();
    }

    /// <summary>A single nutrient at risk with recommended compensating foods.</summary>
    public class NutrientGapEntry
    {
        /// <summary>Nutrient name (e.g., "Calcium", "Vitamin B12", "Omega-3").</summary>
        public string Nutrient { get; set; } = string.Empty;

        /// <summary>
        /// Risk severity:
        ///   LOW      — one minor dietary source excluded
        ///   MODERATE — primary source excluded or two sources excluded
        ///   HIGH     — primary source excluded AND secondary source excluded
        ///   CRITICAL — most dietary sources excluded due to multiple allergens
        /// </summary>
        public string RiskLevel { get; set; } = string.Empty;

        /// <summary>Which of the patient's allergens are causing this gap.</summary>
        public List<string> TriggeringAllergens { get; set; } = new();

        /// <summary>Clinical rationale explaining the risk.</summary>
        public string Rationale { get; set; } = string.Empty;

        /// <summary>Recommended daily intake reference (e.g., "1000 mg/day adults 19–50").</summary>
        public string RdaReference { get; set; } = string.Empty;

        /// <summary>
        /// Foods that provide this nutrient and are safe for the patient's allergen profile.
        /// Filtered for low nickel when NickelSensitive = true.
        /// </summary>
        public List<CompensatingFoodEntry> CompensatingFoods { get; set; } = new();

        /// <summary>
        /// Number of candidate compensating foods that were excluded because they
        /// contained the patient's allergens.
        /// </summary>
        public int ExcludedFoodCount { get; set; }

        /// <summary>
        /// Number of candidate compensating foods excluded specifically due to nickel
        /// content (only populated when NickelSensitive = true).
        /// </summary>
        public int NickelExcludedCount { get; set; }
    }

    /// <summary>A food item recommended as a source of a deficient nutrient.</summary>
    public class CompensatingFoodEntry
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Confirmed allergen-safe for the patient's full allergen profile.</summary>
        public bool AllergenSafe { get; set; }

        /// <summary>True when this food was found and verified in the GlobalIngredients DB.</summary>
        public bool MatchedInDatabase { get; set; }

        /// <summary>Nickel classification from Rebelytics v9.1.1 data.</summary>
        public string NickelClassification { get; set; } = "Unknown";

        /// <summary>Mean nickel content per serving in μg (null if no data).</summary>
        public decimal? NickelMcgPerServing { get; set; }

        /// <summary>Nutritional category (vegetable, protein, grain, etc.).</summary>
        public string? NutritionalCategory { get; set; }

        /// <summary>Whether the food is verified/curated in the database.</summary>
        public bool IsVerified { get; set; }

        /// <summary>
        /// USDA macronutrient data per 100g (null if not available in NutritionCache).
        /// </summary>
        public FoodMacros? Macros { get; set; }

        /// <summary>
        /// Dietary flags from the database (e.g., "vegan", "gluten-free", "halal").
        /// </summary>
        public List<string> DietaryFlags { get; set; } = new();

        /// <summary>
        /// True when no nickel data exists for this food.
        /// Treat as unvalidated when NickelSensitive = true.
        /// </summary>
        public bool NickelDataMissing { get; set; }
    }

    public class FoodMacros
    {
        public decimal CaloriesPer100g { get; set; }
        public decimal ProteinPer100g { get; set; }
        public decimal CarbsPer100g { get; set; }
        public decimal FatPer100g { get; set; }
        public decimal FiberPer100g { get; set; }
    }
}
