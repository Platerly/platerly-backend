namespace Platerly.Api.DTOs
{
    // ── Request ────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/b2b/recipe-analysis/complete
    ///
    /// Provide either a known Recipzee recipeId (all analyses) OR a raw ingredient
    /// list (safety + nutrition; sustainability is skipped without a stored recipe).
    /// </summary>
    public class CompleteRecipeAnalysisRequest
    {
        /// <summary>Known Recipzee recipe ID. Enables full analysis including sustainability.</summary>
        public Guid? RecipeId { get; set; }

        /// <summary>
        /// Raw ingredient names for ad-hoc analysis (max 200).
        /// Required when RecipeId is not provided.
        /// </summary>
        public List<string>? Ingredients { get; set; }

        /// <summary>Optional display label for the recipe.</summary>
        public string? Title { get; set; }

        /// <summary>Number of servings for nutrition calculation (default 1, max 100).</summary>
        public int Servings { get; set; } = 1;

        /// <summary>
        /// Which analysis modules to include. Defaults to all available.
        /// Valid values: "safety", "nutrition", "sustainability"
        /// Omit or pass null/empty to run all.
        /// </summary>
        public List<string>? Modules { get; set; }
    }

    // ── Response ───────────────────────────────────────────────────────────────

    public class CompleteRecipeAnalysisResponse
    {
        /// <summary>Recipe title (from request or fetched from DB).</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Allergen and prohibited ingredient audit results.</summary>
        public RecipeSafetyAuditResponse? Safety { get; set; }

        /// <summary>Per-serving and total nutrition estimates.</summary>
        public NutritionEstimateResponse? Nutrition { get; set; }

        /// <summary>
        /// Sustainability score and carbon/water breakdown.
        /// Null when request was made with a raw ingredient list (no stored recipe).
        /// </summary>
        public RecipeSustainabilityResponse? Sustainability { get; set; }

        /// <summary>Metadata about what was analysed and how long it took.</summary>
        public AnalysisMetadata Metadata { get; set; } = new();
    }

    public class AnalysisMetadata
    {
        /// <summary>Wall-clock time for the full analysis in milliseconds.</summary>
        public long AnalysisTimeMs { get; set; }

        /// <summary>Number of ingredients submitted or resolved from the recipe.</summary>
        public int IngredientCount { get; set; }

        /// <summary>Which modules were executed successfully.</summary>
        public List<string> ModulesExecuted { get; set; } = new();

        /// <summary>
        /// Modules that were requested but could not be executed
        /// (e.g. sustainability requires a stored recipeId).
        /// </summary>
        public List<string> ModulesSkipped { get; set; } = new();

        /// <summary>
        /// Any non-fatal issues encountered during analysis
        /// (e.g. a specific module returned null unexpectedly).
        /// </summary>
        public List<string> Warnings { get; set; } = new();
    }
}
