namespace Platerly.Api.DTOs
{
    // ========== Dietary Tag Deep Dive ==========

    public class DietaryTagDetailDto
    {
        public string Tag { get; set; } = string.Empty;
        public int TotalRecipes { get; set; }
        public int RecipesLast30Days { get; set; }
        public int RecipesLast90Days { get; set; }
        public decimal GrowthRateLast30Days { get; set; }
        public List<string> TopIngredients { get; set; } = new();
        public List<string> TopRegions { get; set; } = new();
    }

    public class DietaryTagDetailResponse
    {
        public DietaryTagDetailDto Tag { get; set; } = new();
    }

    // ========== Dietary Crossover ==========

    public class DietaryCrossoverDto
    {
        public string TagA { get; set; } = string.Empty;
        public string TagB { get; set; } = string.Empty;
        public int OverlapCount { get; set; }
        public int TagATotal { get; set; }
        public int TagBTotal { get; set; }
        public decimal OverlapPercentageA { get; set; }
        public decimal OverlapPercentageB { get; set; }
    }

    public class DietaryCrossoverResponse
    {
        public List<DietaryCrossoverDto> Crossovers { get; set; } = new();
        public int TotalTagsAnalyzed { get; set; }
    }
}
