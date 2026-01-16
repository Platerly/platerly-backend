namespace Platerly.Api.DTOs
{
    // ========== Ingredient Trends ==========

    public class IngredientTrendDto
    {
        public Guid GlobalIngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public string? Category { get; set; }
        public DateOnly Date { get; set; }
        public string? Region { get; set; }
        public int UsageCount { get; set; }
        public int UniqueRecipes { get; set; }
        public int UniqueUsers { get; set; }
        public decimal? DayOverDayChange { get; set; }
        public decimal? WeekOverWeekChange { get; set; }
        public int TrendDirection { get; set; } // -1 = declining, 0 = stable, 1 = rising
        public int? PopularityRank { get; set; }
    }

    public class TrendingIngredientsRequest
    {
        public string? Region { get; set; }
        public int Days { get; set; } = 7;
        public int Take { get; set; } = 20;
        public string? Category { get; set; }
    }

    public class TrendingIngredientsResponse
    {
        public List<IngredientTrendDto> Trends { get; set; } = new();
        public string? Region { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public int TotalIngredients { get; set; }
    }

    // ========== Regional Statistics ==========

    public class RegionalIngredientStatsDto
    {
        public Guid GlobalIngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string Region { get; set; } = string.Empty;
        public long TotalUsageCount { get; set; }
        public decimal PenetrationRate { get; set; }
        public decimal? AverageQuantity { get; set; }
        public string? CommonUnit { get; set; }
        public int PopularityRank { get; set; }
        public string? SeasonalPeakMonths { get; set; }
        public string? CommonPairings { get; set; }
    }

    public class RegionalStatsRequest
    {
        public string Region { get; set; } = string.Empty;
        public int Take { get; set; } = 50;
        public string? Category { get; set; }
    }

    public class RegionalStatsResponse
    {
        public string Region { get; set; } = string.Empty;
        public List<RegionalIngredientStatsDto> Stats { get; set; } = new();
        public int TotalIngredientsInRegion { get; set; }
        public long TotalUsagesInRegion { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    // ========== Ingredient Pairings ==========

    public class IngredientPairingDto
    {
        public Guid IngredientAId { get; set; }
        public string IngredientAName { get; set; } = string.Empty;
        public Guid IngredientBId { get; set; }
        public string IngredientBName { get; set; } = string.Empty;
        public int CoOccurrenceCount { get; set; }
        public decimal AffinityScore { get; set; }
        public string? TopRegion { get; set; }
        public string? CommonCuisines { get; set; }
    }

    public class PairingsRequest
    {
        public Guid? IngredientId { get; set; }
        public int Take { get; set; } = 20;
        public decimal? MinAffinityScore { get; set; }
    }

    public class PairingsResponse
    {
        public Guid? ForIngredientId { get; set; }
        public string? ForIngredientName { get; set; }
        public List<IngredientPairingDto> Pairings { get; set; } = new();
        public int TotalPairings { get; set; }
    }

    // ========== Popular Ingredients ==========

    public class PopularIngredientDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Category { get; set; }
        public long TotalUsageCount { get; set; }
        public int UniqueRecipeCount { get; set; }
        public int? PopularityRank { get; set; }
        public string? PrimaryRegion { get; set; }
        public string? DietaryFlags { get; set; }
        public decimal? AverageRecipeRating { get; set; }
    }

    public class PopularIngredientsRequest
    {
        public string? Region { get; set; }
        public string? Category { get; set; }
        public int Take { get; set; } = 50;
        public string? DietaryFilter { get; set; } // e.g., "vegan", "halal"
    }

    public class PopularIngredientsResponse
    {
        public List<PopularIngredientDto> Ingredients { get; set; } = new();
        public string? Region { get; set; }
        public string? Category { get; set; }
        public int TotalCount { get; set; }
    }

    // ========== Search Demand Signals ==========

    public class SearchDemandDto
    {
        public string Query { get; set; } = string.Empty;
        public int SearchCount { get; set; }
        public bool WasFound { get; set; }
        public string? Region { get; set; }
        public string? SearchContext { get; set; }
    }

    public class SearchDemandRequest
    {
        public int Days { get; set; } = 30;
        public int Take { get; set; } = 50;
        public string? Region { get; set; }
        public bool? OnlyUnfound { get; set; } // Show only searches that found no results
    }

    public class SearchDemandResponse
    {
        public List<SearchDemandDto> Searches { get; set; } = new();
        public int TotalSearches { get; set; }
        public int UnfoundSearches { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
    }

    // ========== Dietary Trends ==========

    public class DietaryTrendDto
    {
        public string DietaryTag { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public string? Region { get; set; }
        public int RecipeCount { get; set; }
        public int UniqueUsers { get; set; }
        public decimal? GrowthRate { get; set; }
        public decimal MarketShare { get; set; }
    }

    public class DietaryTrendsRequest
    {
        public int Days { get; set; } = 30;
        public string? Region { get; set; }
        public string? DietaryTag { get; set; } // Filter to specific tag
    }

    public class DietaryTrendsResponse
    {
        public List<DietaryTrendDto> Trends { get; set; } = new();
        public string? Region { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
    }

    // ========== Category Analytics ==========

    public class CategoryAnalyticsDto
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public int? ParentCategoryId { get; set; }
        public string? ParentCategoryName { get; set; }
        public int IngredientCount { get; set; }
        public long TotalUsageCount { get; set; }
        public int UniqueRecipeCount { get; set; }
        public string? TopIngredients { get; set; } // Comma-separated top 5
    }

    public class CategoryAnalyticsResponse
    {
        public List<CategoryAnalyticsDto> Categories { get; set; } = new();
        public int TotalCategories { get; set; }
    }
}
