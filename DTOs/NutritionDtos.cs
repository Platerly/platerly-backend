namespace Platerly.Api.DTOs
{
    // ========== Recipe Nutrition ==========

    public class RecipeNutritionResponse
    {
        public Guid RecipeId { get; set; }
        public string RecipeTitle { get; set; } = string.Empty;
        public int Servings { get; set; }
        public NutritionBreakdown PerServing { get; set; } = new();
        public NutritionBreakdown Total { get; set; } = new();
        public List<IngredientNutritionDto> Ingredients { get; set; } = new();
        public int MatchedIngredients { get; set; }
        public int TotalIngredients { get; set; }
    }

    public class NutritionBreakdown
    {
        public decimal Calories { get; set; }
        public decimal ProteinG { get; set; }
        public decimal CarbsG { get; set; }
        public decimal FatG { get; set; }
        public decimal FiberG { get; set; }
        public decimal SodiumMg { get; set; }
        public decimal SugarG { get; set; }
        public decimal SaturatedFatG { get; set; }
        public decimal CholesterolMg { get; set; }
    }

    public class IngredientNutritionDto
    {
        public string IngredientName { get; set; } = string.Empty;
        public string? Quantity { get; set; }
        public string? Unit { get; set; }
        public decimal EstimatedGrams { get; set; }
        public NutritionBreakdown Nutrition { get; set; } = new();
        public int MatchConfidence { get; set; }
        public bool Matched { get; set; }
    }

    // ========== Estimate from Ingredient List ==========

    public class NutritionEstimateRequest
    {
        public List<NutritionIngredientInput> Ingredients { get; set; } = new();
        public int Servings { get; set; } = 1;
    }

    public class NutritionIngredientInput
    {
        public string Name { get; set; } = string.Empty;
        public string? Quantity { get; set; }
        public string? Unit { get; set; }
    }

    public class NutritionEstimateResponse
    {
        public int Servings { get; set; }
        public NutritionBreakdown PerServing { get; set; } = new();
        public NutritionBreakdown Total { get; set; } = new();
        public List<IngredientNutritionDto> Ingredients { get; set; } = new();
        public int MatchedIngredients { get; set; }
        public int TotalIngredients { get; set; }
    }
}
