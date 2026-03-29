namespace Platerly.Api.DTOs
{
    // ========== Recipe Safety Audit ==========

    public class RecipeSafetyAuditRequest
    {
        public List<string> Ingredients { get; set; } = new();

        /// <summary>
        /// Optional. When provided, allergen flags are filtered to only include
        /// allergens matching this profile (e.g. ["nickel", "tree nuts"]).
        /// Max 30 entries.
        /// </summary>
        public List<string>? AllergenProfile { get; set; }
    }

    public class RecipeSafetyAuditResponse
    {
        public int TotalIngredients { get; set; }
        public int FlaggedIngredients { get; set; }
        public string OverallRiskLevel { get; set; } = "Low"; // None, Low, Medium, High
        public List<SafetyFlagDto> AllergenFlags { get; set; } = new();
        public List<SafetyFlagDto> ProhibitedFlags { get; set; } = new();
        public List<SafetyFlagDto> CrossContactFlags { get; set; } = new();
        public List<string> SafeIngredients { get; set; } = new();
        public string Disclaimer { get; set; } = string.Empty;
    }

    public class SafetyFlagDto
    {
        public string IngredientName { get; set; } = string.Empty;
        public string FlagType { get; set; } = string.Empty; // Allergen, Prohibited, CrossContact
        public string Detail { get; set; } = string.Empty;
        public string Severity { get; set; } = "Low";

        /// <summary>
        /// Top 2 safe substitute suggestions for this flagged ingredient.
        /// Populated when AllergenProfile is provided in the request.
        /// </summary>
        public List<SafeSubstituteSuggestion> Substitutions { get; set; } = new();
    }

    public class SafeSubstituteSuggestion
    {
        public string SubstituteName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public decimal ConversionRatio { get; set; }
        public int ConfidenceScore { get; set; }
        public bool IsVerified { get; set; }
        public string? Notes { get; set; }
        public string? NickelSafetyNote { get; set; }
    }

    // ========== Batch Audit ==========

    public class BatchAuditRequest
    {
        public List<BatchAuditRecipeInput> Recipes { get; set; } = new();
    }

    public class BatchAuditRecipeInput
    {
        public string? Label { get; set; }
        public List<string> Ingredients { get; set; } = new();
        public List<string>? AllergenProfile { get; set; }
    }

    public class BatchAuditResponse
    {
        public int TotalRecipes { get; set; }
        public int RecipesWithFlags { get; set; }
        public List<BatchAuditRecipeResult> Results { get; set; } = new();
    }

    public class BatchAuditRecipeResult
    {
        public string? Label { get; set; }
        public RecipeSafetyAuditResponse Audit { get; set; } = new();
    }
}
