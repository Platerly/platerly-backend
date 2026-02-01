namespace Platerly.Api.DTOs
{
    // ========== Shopping Patterns ==========

    public class ShoppingPatternDto
    {
        public string IngredientName { get; set; } = string.Empty;
        public string? Category { get; set; }
        public int ListAppearances { get; set; }
        public decimal CheckedOffRate { get; set; }
        public decimal AverageQuantity { get; set; }
        public string? CommonUnit { get; set; }
    }

    public class ShoppingPatternsResponse
    {
        public List<ShoppingPatternDto> TopIngredients { get; set; } = new();
        public decimal AverageListSize { get; set; }
        public decimal AverageCheckedOffRate { get; set; }
        public int TotalListsAnalyzed { get; set; }
        public Dictionary<string, int> CategoryDistribution { get; set; } = new();
    }

    // ========== Meal Planning Patterns ==========

    public class MealPlanningPatternsResponse
    {
        public Dictionary<string, int> MealTypeDistribution { get; set; } = new();
        public decimal AveragePlanDurationDays { get; set; }
        public decimal AverageRecipesPerPlan { get; set; }
        public int TotalPlansAnalyzed { get; set; }
        public List<PopularMealPlanRecipeDto> TopRecipes { get; set; } = new();
    }

    public class PopularMealPlanRecipeDto
    {
        public Guid RecipeId { get; set; }
        public string RecipeTitle { get; set; } = string.Empty;
        public string? Region { get; set; }
        public int PlanAppearances { get; set; }
        public string MostCommonMealType { get; set; } = string.Empty;
    }
}
