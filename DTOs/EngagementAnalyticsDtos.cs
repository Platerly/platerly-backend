namespace Platerly.Api.DTOs
{
    // ========== Engagement Overview ==========

    public class EngagementOverviewResponse
    {
        public long TotalRecipes { get; set; }
        public long TotalViews { get; set; }
        public long TotalLikes { get; set; }
        public long TotalComments { get; set; }
        public long TotalReposts { get; set; }
        public long TotalSaves { get; set; }
        public decimal AvgViewsPerRecipe { get; set; }
        public decimal AvgLikesPerRecipe { get; set; }
        public decimal AvgCommentsPerRecipe { get; set; }
    }

    // ========== Engagement by Category ==========

    public class EngagementByCategoryDto
    {
        public string Category { get; set; } = string.Empty;
        public int RecipeCount { get; set; }
        public long TotalViews { get; set; }
        public long TotalLikes { get; set; }
        public long TotalComments { get; set; }
        public decimal AvgEngagementRate { get; set; }
    }

    public class EngagementByCategoryResponse
    {
        public List<EngagementByCategoryDto> ByType { get; set; } = new();
        public List<EngagementByDifficultyDto> ByDifficulty { get; set; } = new();
    }

    public class EngagementByDifficultyDto
    {
        public string Difficulty { get; set; } = string.Empty;
        public int RecipeCount { get; set; }
        public long TotalViews { get; set; }
        public long TotalLikes { get; set; }
        public decimal AvgEngagementRate { get; set; }
    }

    // ========== Engagement by Region ==========

    public class EngagementByRegionDto
    {
        public string Region { get; set; } = string.Empty;
        public int RecipeCount { get; set; }
        public long TotalViews { get; set; }
        public long TotalLikes { get; set; }
        public long TotalComments { get; set; }
        public long TotalReposts { get; set; }
        public decimal AvgEngagementRate { get; set; }
    }

    public class EngagementByRegionResponse
    {
        public List<EngagementByRegionDto> Regions { get; set; } = new();
    }
}
