namespace Platerly.Api.DTOs
{
    // ── Public search request ──────────────────────────────────────────────────

    public class RecipeSearchRequest
    {
        /// <summary>Free-text search on recipe title. Partial match, case-insensitive.</summary>
        public string? Query { get; set; }

        /// <summary>Filter by cuisine type (e.g. "British", "Indian").</summary>
        public string? Cuisine { get; set; }

        /// <summary>
        /// Maximum nickel risk level to include.
        /// "SAFE" = SAFE only. "MODERATE" = SAFE + MODERATE (default). "EXCEEDED" = all.
        /// </summary>
        public string? MaxNickelLoad { get; set; }

        /// <summary>
        /// Comma-separated allergen names to exclude (e.g. "peanuts,tree nuts,gluten").
        /// Only excludes recipes that have been analysed and have a flag for that allergen.
        /// </summary>
        public string? ExcludeAllergens { get; set; }

        /// <summary>Maximum total prep + cook time in minutes.</summary>
        public int? PrepTimeMax { get; set; }

        public int Page     { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // ── Public search response ─────────────────────────────────────────────────

    public class RecipeSearchResponse
    {
        public int TotalCount { get; set; }
        public int Page       { get; set; }
        public int PageSize   { get; set; }
        public int TotalPages { get; set; }
        public List<RecipeSearchResult> Results { get; set; } = new();
    }

    public class RecipeSearchResult
    {
        public Guid    Id             { get; set; }
        public string  Title          { get; set; } = string.Empty;
        public string? CuisineType    { get; set; }
        public string? ThumbnailUrl   { get; set; }
        public string  SourceDomain   { get; set; } = string.Empty;
        public string  SourceUrl      { get; set; } = string.Empty;
        public int?    PrepTimeMinutes { get; set; }
        public int?    CookTimeMinutes { get; set; }
        public List<string> Tags      { get; set; } = new();
        public int IngredientCount    { get; set; }

        /// <summary>Whether the allergen + nickel analysis pipeline has run on this recipe.</summary>
        public bool IsAnalysed        { get; set; }

        /// <summary>"SAFE", "MODERATE", "EXCEEDED", or "Unknown" when not yet analysed.</summary>
        public string NickelRiskLevel { get; set; } = "Unknown";

        public RecipeAllergenSummary AllergenSummary { get; set; } = new();
    }

    public class RecipeAllergenSummary
    {
        /// <summary>"None", "Low", "Medium", "High" — from RecipeSafetyAuditResponse.</summary>
        public string OverallRiskLevel { get; set; } = "Unknown";

        /// <summary>Distinct allergen names flagged (e.g. ["Gluten", "Dairy"]).</summary>
        public List<string> FlaggedAllergens { get; set; } = new();
    }

    // ── Recipe detail response ─────────────────────────────────────────────────

    public class RecipeDetailResponse
    {
        public Guid    Id              { get; set; }
        public string  Title           { get; set; } = string.Empty;
        public string? CuisineType     { get; set; }
        public string? ThumbnailUrl    { get; set; }
        public string  SourceDomain    { get; set; } = string.Empty;
        public string  SourceUrl       { get; set; } = string.Empty;
        public string? OriginalAuthor  { get; set; }
        public int?    PrepTimeMinutes { get; set; }
        public int?    CookTimeMinutes { get; set; }
        public List<string> Tags         { get; set; } = new();
        public List<string> Ingredients  { get; set; } = new();
        public List<string> Instructions { get; set; } = new();
        public bool   IsAnalysed       { get; set; }
        public string? AnalysisVersion { get; set; }
        public string  NickelRiskLevel { get; set; } = "Unknown";
        public RecipeAllergenSummary AllergenSummary { get; set; } = new();

        /// <summary>Full allergen audit payload for power consumers.</summary>
        public System.Text.Json.JsonElement? AllergenAnalysis { get; set; }

        /// <summary>Full nickel load payload for power consumers.</summary>
        public System.Text.Json.JsonElement? NickelLoad { get; set; }

        /// <summary>Always true — non-halal recipes are never stored.</summary>
        public bool   HalalVerified { get; set; }
        public string HalalNote     { get; set; } = string.Empty;
    }

    /// <summary>
    /// Statistics returned after one run of the analysis pipeline.
    /// </summary>
    public class AnalysisPipelineStats
    {
        public int TotalProcessed { get; set; }
        public int TotalFailed    { get; set; }
        public DateTime StartedAt   { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public long ElapsedMs => CompletedAt.HasValue
            ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds : 0;
    }

    /// <summary>
    /// Statistics returned after one run of the recipe indexing pipeline.
    /// </summary>
    public class IndexingRunStats
    {
        public string Source { get; set; } = string.Empty;
        /// <summary>Total recipes fetched from the external API (before gate).</summary>
        public int TotalFetched { get; set; }
        /// <summary>Recipes that passed the Halal Gate and were stored.</summary>
        public int TotalStored { get; set; }
        /// <summary>Recipes that failed the Halal Gate and were discarded.</summary>
        public int TotalDiscarded { get; set; }
        /// <summary>Recipes skipped because they already exist in the index.</summary>
        public int TotalSkipped { get; set; }
        /// <summary>Titles of discarded recipes (for logging).</summary>
        public List<string> DiscardedTitles { get; set; } = new();
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public long ElapsedMs => CompletedAt.HasValue
            ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds : 0;
    }
}
