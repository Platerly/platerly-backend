namespace Platerly.Api.DTOs
{
    public class WidgetConfigRequest
    {
        public string? ThemeJson { get; set; }
        public int MaxRecipes { get; set; } = 10;
        public string? DietaryFilters { get; set; }
        public string? RegionFilter { get; set; }
        public string? LogoUrl { get; set; }
    }

    public class WidgetConfigResponse
    {
        public Guid Id { get; set; }
        public string? ThemeJson { get; set; }
        public int MaxRecipes { get; set; }
        public string? DietaryFilters { get; set; }
        public string? RegionFilter { get; set; }
        public string? LogoUrl { get; set; }
        public bool IsActive { get; set; }
        public string EmbedUrl { get; set; } = string.Empty;
    }

    public class WidgetFeedRecipeDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string? Region { get; set; }
        public int PrepTimeMinutes { get; set; }
        public int CookTimeMinutes { get; set; }
        public string? Difficulty { get; set; }
        public List<string> Tags { get; set; } = new();
        public string AuthorUsername { get; set; } = string.Empty;
    }

    public class WidgetFeedResponse
    {
        public List<WidgetFeedRecipeDto> Recipes { get; set; } = new();
        public int TotalAvailable { get; set; }
    }
}
