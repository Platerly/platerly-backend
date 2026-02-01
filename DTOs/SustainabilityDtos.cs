namespace Platerly.Api.DTOs
{
    public class RecipeSustainabilityResponse
    {
        public Guid RecipeId { get; set; }
        public string RecipeTitle { get; set; } = string.Empty;
        public int OverallScore { get; set; }
        public string Rating { get; set; } = string.Empty; // A, B, C, D, E
        public decimal TotalCarbonKg { get; set; }
        public decimal TotalWaterLiters { get; set; }
        public int SeasonalIngredients { get; set; }
        public int TotalIngredients { get; set; }
        public int MatchedIngredients { get; set; }
        public List<IngredientSustainabilityDto> Ingredients { get; set; } = new();
    }

    public class IngredientSustainabilityDto
    {
        public string IngredientName { get; set; } = string.Empty;
        public int OverallScore { get; set; }
        public decimal CarbonScorePer100g { get; set; }
        public decimal WaterUsagePer100g { get; set; }
        public bool IsSeasonal { get; set; }
        public int TransportScore { get; set; }
        public bool Matched { get; set; }
    }

    public class SustainabilityCompareRequest
    {
        public List<Guid> RecipeIds { get; set; } = new();
    }

    public class SustainabilityCompareItemDto
    {
        public Guid RecipeId { get; set; }
        public string RecipeTitle { get; set; } = string.Empty;
        public int OverallScore { get; set; }
        public string Rating { get; set; } = string.Empty;
        public decimal TotalCarbonKg { get; set; }
        public decimal TotalWaterLiters { get; set; }
    }

    public class SustainabilityCompareResponse
    {
        public List<SustainabilityCompareItemDto> Recipes { get; set; } = new();
    }
}
