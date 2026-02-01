namespace Platerly.Api.DTOs
{
    // ========== Menu Analysis ==========

    public class MenuAnalysisRequest
    {
        public List<Guid> RecipeIds { get; set; } = new();
    }

    public class MenuAnalysisResponse
    {
        public int TotalRecipes { get; set; }
        public decimal DiversityScore { get; set; }
        public List<string> AllergensCovered { get; set; } = new();
        public List<string> AllergensNotCovered { get; set; } = new();
        public Dictionary<string, int> DietaryTagDistribution { get; set; } = new();
        public Dictionary<string, int> RegionDistribution { get; set; } = new();
        public Dictionary<string, int> TypeDistribution { get; set; } = new();
        public List<string> MissingPopularIngredients { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    // ========== Menu Suggestions ==========

    public class MenuSuggestionsRequest
    {
        public List<string>? DietaryTags { get; set; }
        public string? Region { get; set; }
        public List<string>? ExcludeAllergens { get; set; }
        public int Take { get; set; } = 10;
    }

    public class MenuSuggestionDto
    {
        public Guid RecipeId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? Difficulty { get; set; }
        public List<string> DietaryTags { get; set; } = new();
        public decimal EngagementScore { get; set; }
    }

    public class MenuSuggestionsResponse
    {
        public List<MenuSuggestionDto> Suggestions { get; set; } = new();
        public string? AppliedRegionFilter { get; set; }
        public List<string>? AppliedDietaryFilters { get; set; }
        public List<string>? ExcludedAllergens { get; set; }
    }
}
