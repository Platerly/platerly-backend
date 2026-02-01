namespace Platerly.Api.DTOs
{
    // ========== Recipe Safety Audit ==========

    public class RecipeSafetyAuditRequest
    {
        public List<string> Ingredients { get; set; } = new();
    }

    public class RecipeSafetyAuditResponse
    {
        public int TotalIngredients { get; set; }
        public int FlaggedIngredients { get; set; }
        public string OverallRisk { get; set; } = "Low"; // Low, Medium, High
        public List<SafetyFlagDto> AllergenFlags { get; set; } = new();
        public List<SafetyFlagDto> ProhibitedFlags { get; set; } = new();
        public List<SafetyFlagDto> CrossContactFlags { get; set; } = new();
    }

    public class SafetyFlagDto
    {
        public string IngredientName { get; set; } = string.Empty;
        public string FlagType { get; set; } = string.Empty; // Allergen, Prohibited, CrossContact
        public string Detail { get; set; } = string.Empty;
        public string Severity { get; set; } = "Low";
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
