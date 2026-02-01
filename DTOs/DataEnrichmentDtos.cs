namespace Platerly.Api.DTOs
{
    // ========== Ingredient Enrichment ==========

    public class IngredientEnrichmentRequest
    {
        public List<string> IngredientNames { get; set; } = new();
    }

    public class EnrichedIngredientDto
    {
        public string InputName { get; set; } = string.Empty;
        public bool Found { get; set; }
        public Guid? GlobalIngredientId { get; set; }
        public string? MatchedName { get; set; }
        public string? Category { get; set; }
        public List<string> Allergens { get; set; } = new();
        public List<string> DietaryFlags { get; set; } = new();
        public List<EnrichedSubstituteDto> TopSubstitutes { get; set; } = new();
    }

    public class EnrichedSubstituteDto
    {
        public string Name { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public int ConfidenceScore { get; set; }
    }

    public class IngredientEnrichmentResponse
    {
        public List<EnrichedIngredientDto> Ingredients { get; set; } = new();
        public int MatchedCount { get; set; }
        public int TotalCount { get; set; }
    }

    // ========== Recipe Enrichment ==========

    public class RecipeEnrichmentRequest
    {
        public string? Title { get; set; }
        public List<string> Ingredients { get; set; } = new();
        public string? Region { get; set; }
    }

    public class RecipeEnrichmentResponse
    {
        public List<string> DietaryTags { get; set; } = new();
        public List<AllergenWarningDto> AllergenWarnings { get; set; } = new();
        public string? EstimatedDifficulty { get; set; }
        public string? EstimatedCuisine { get; set; }
        public int IngredientCount { get; set; }
        public int RecognizedIngredients { get; set; }
        public List<string> Categories { get; set; } = new();
    }

    public class AllergenWarningDto
    {
        public string IngredientName { get; set; } = string.Empty;
        public string AllergenType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public bool MayContain { get; set; }
    }
}
