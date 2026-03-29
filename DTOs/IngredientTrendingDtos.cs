namespace Platerly.Api.DTOs
{
    // ── Request ────────────────────────────────────────────────────────────────

    public class IngredientTrendingRequest
    {
        /// <summary>Optional region filter (e.g., "Mediterranean", "Asian").</summary>
        public string? Region { get; set; }

        /// <summary>Optional ingredient category slug filter.</summary>
        public string? Category { get; set; }

        /// <summary>
        /// Trend direction filter: "rising" (TrendDirection=1), "declining" (-1),
        /// "stable" (0). Omit for all.
        /// </summary>
        public string? Trend { get; set; }

        /// <summary>
        /// Number of calendar days to look back for aggregation (default 30, max 90).
        /// </summary>
        public int Days { get; set; } = 30;

        /// <summary>Number of results to return (default 50, max 200).</summary>
        public int Take { get; set; } = 50;

        /// <summary>
        /// Sort order: "usage" (total usage count, default), "rank" (popularity rank),
        /// "growth" (week-over-week change descending).
        /// </summary>
        public string SortBy { get; set; } = "usage";
    }

    // ── Response ───────────────────────────────────────────────────────────────

    public class IngredientTrendingResponse
    {
        /// <summary>Period covered by this response.</summary>
        public IngredientTrendingPeriod Period { get; set; } = new();

        /// <summary>Ranked trending ingredients for the period.</summary>
        public List<B2BIngredientTrendDto> Ingredients { get; set; } = new();

        public int TotalReturned => Ingredients.Count;
    }

    public class IngredientTrendingPeriod
    {
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public int Days { get; set; }
        public string? Region { get; set; }
    }

    public class B2BIngredientTrendDto
    {
        public Guid IngredientId { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>Ingredient category name (e.g., "Vegetables", "Dairy").</summary>
        public string? Category { get; set; }

        /// <summary>Total usage count across the period.</summary>
        public int TotalUsageCount { get; set; }

        /// <summary>Unique recipes containing this ingredient.</summary>
        public int UniqueRecipes { get; set; }

        /// <summary>Unique users who used this ingredient.</summary>
        public int UniqueUsers { get; set; }

        /// <summary>Popularity rank (1 = most popular). Null if unranked.</summary>
        public int? PopularityRank { get; set; }

        /// <summary>Average day-over-day change % across the period.</summary>
        public decimal? AvgDayOverDayChange { get; set; }

        /// <summary>Average week-over-week change % across the period.</summary>
        public decimal? AvgWeekOverWeekChange { get; set; }

        /// <summary>
        /// Dominant trend direction across the period:
        /// "rising" (+1), "stable" (0), "declining" (-1).
        /// </summary>
        public string TrendDirection { get; set; } = "stable";

        /// <summary>Nickel classification (Low, Moderate, High, Unknown).</summary>
        public string? NickelClassification { get; set; }

        /// <summary>Regional stats for the requested region (when Region filter applied).</summary>
        public IngredientRegionalStatsDto? RegionalStats { get; set; }
    }

    public class IngredientRegionalStatsDto
    {
        public string Region { get; set; } = string.Empty;
        public long TotalUsageCount { get; set; }
        public decimal PenetrationRate { get; set; }
        public int PopularityRank { get; set; }
        public string? SeasonalPeakMonths { get; set; }
        public string? CommonDietaryContexts { get; set; }
    }
}
