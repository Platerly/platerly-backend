namespace Platerly.Api.DTOs
{
    // ========== Allergen Prevalence ==========

    public class AllergenPrevalenceDto
    {
        public Guid AllergyId { get; set; }
        public string AllergyName { get; set; } = string.Empty;
        public int UserCount { get; set; }
        public decimal Percentage { get; set; }
        public int LowSeverityCount { get; set; }
        public int MediumSeverityCount { get; set; }
        public int HighSeverityCount { get; set; }
        public int IncludeTraceCount { get; set; }
    }

    public class AllergenPrevalenceResponse
    {
        public List<AllergenPrevalenceDto> Allergens { get; set; } = new();
        public int TotalUsersWithAllergies { get; set; }
        public int TotalUsers { get; set; }
    }

    // ========== Ingredients by Allergen ==========

    public class AllergenIngredientDto
    {
        public Guid GlobalIngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string AllergenType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public bool MayContain { get; set; }
        public bool CrossContactRisk { get; set; }
        public bool ProcessingAid { get; set; }
        public string? ENumber { get; set; }
        public string? Notes { get; set; }
    }

    public class AllergenIngredientsRequest
    {
        public string AllergenType { get; set; } = string.Empty;
        public int Take { get; set; } = 50;
        public string? SeverityFilter { get; set; }
    }

    public class AllergenIngredientsResponse
    {
        public string AllergenType { get; set; } = string.Empty;
        public List<AllergenIngredientDto> Ingredients { get; set; } = new();
        public int TotalIngredients { get; set; }
    }

    // ========== Safe Ingredients ==========

    public class SafeIngredientDto
    {
        public Guid GlobalIngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public string? Category { get; set; }
    }

    public class SafeIngredientsRequest
    {
        public string Allergens { get; set; } = string.Empty;
        public bool ExcludeMayContain { get; set; } = true;
        public bool ExcludeCrossContact { get; set; } = false;
        public int Take { get; set; } = 100;
        public string? Category { get; set; }
    }

    public class SafeIngredientsResponse
    {
        public List<string> ExcludedAllergens { get; set; } = new();
        public List<SafeIngredientDto> Ingredients { get; set; } = new();
        public int TotalSafeIngredients { get; set; }
        public bool ExcludedMayContain { get; set; }
        public bool ExcludedCrossContact { get; set; }
    }
}
