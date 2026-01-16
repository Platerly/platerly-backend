using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Logs every ingredient usage for analytics.
    /// This is the raw data that powers B2B insights.
    /// </summary>
    [Index(nameof(GlobalIngredientId), nameof(CreatedAt))]
    [Index(nameof(Region), nameof(CreatedAt))]
    [Index(nameof(CreatedAt))]
    public class IngredientUsageLog
    {
        public long Id { get; set; }

        public Guid GlobalIngredientId { get; set; }
        public GlobalIngredient GlobalIngredient { get; set; } = null!;

        public Guid RecipeId { get; set; }
        public Recipe Recipe { get; set; } = null!;

        /// <summary>
        /// User who added the recipe
        /// </summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// Region from recipe (denormalized for query performance)
        /// </summary>
        [MaxLength(64)]
        public string? Region { get; set; }

        /// <summary>
        /// Country code (ISO 3166-1 alpha-2)
        /// </summary>
        [MaxLength(2)]
        public string? CountryCode { get; set; }

        /// <summary>
        /// City (if available from user profile)
        /// </summary>
        [MaxLength(128)]
        public string? City { get; set; }

        /// <summary>
        /// Quantity used (parsed if possible)
        /// </summary>
        [MaxLength(64)]
        public string? Quantity { get; set; }

        /// <summary>
        /// Parsed numeric quantity (for aggregation)
        /// </summary>
        public decimal? QuantityNumeric { get; set; }

        /// <summary>
        /// Unit of measurement
        /// </summary>
        [MaxLength(32)]
        public string? Unit { get; set; }

        /// <summary>
        /// Category ID at time of logging (denormalized)
        /// </summary>
        public int? CategoryId { get; set; }

        /// <summary>
        /// Was this a substitution for something else?
        /// </summary>
        public Guid? SubstitutedForIngredientId { get; set; }

        /// <summary>
        /// Dietary tags from recipe (denormalized)
        /// </summary>
        [MaxLength(256)]
        public string? DietaryTags { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Aggregated daily ingredient trends.
    /// Pre-computed for fast B2B API access.
    /// </summary>
    [Index(nameof(GlobalIngredientId), nameof(Date))]
    [Index(nameof(Region), nameof(Date))]
    [Index(nameof(Date))]
    public class IngredientTrendDaily
    {
        public long Id { get; set; }

        public Guid GlobalIngredientId { get; set; }
        public GlobalIngredient GlobalIngredient { get; set; } = null!;

        /// <summary>
        /// The date this trend is for
        /// </summary>
        public DateOnly Date { get; set; }

        /// <summary>
        /// Region (null = global)
        /// </summary>
        [MaxLength(64)]
        public string? Region { get; set; }

        /// <summary>
        /// Number of times used on this date
        /// </summary>
        public int UsageCount { get; set; }

        /// <summary>
        /// Number of unique recipes using this ingredient
        /// </summary>
        public int UniqueRecipes { get; set; }

        /// <summary>
        /// Number of unique users using this ingredient
        /// </summary>
        public int UniqueUsers { get; set; }

        /// <summary>
        /// Change from previous day (percentage)
        /// </summary>
        public decimal? DayOverDayChange { get; set; }

        /// <summary>
        /// Change from same day last week (percentage)
        /// </summary>
        public decimal? WeekOverWeekChange { get; set; }

        /// <summary>
        /// Trend direction: -1 = declining, 0 = stable, 1 = rising
        /// </summary>
        public int TrendDirection { get; set; } = 0;

        /// <summary>
        /// Popularity rank for this date (1 = most popular)
        /// </summary>
        public int? PopularityRank { get; set; }

        public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Regional ingredient statistics for B2B insights.
    /// </summary>
    [Index(nameof(Region), nameof(GlobalIngredientId))]
    public class RegionalIngredientStats
    {
        public int Id { get; set; }

        public Guid GlobalIngredientId { get; set; }
        public GlobalIngredient GlobalIngredient { get; set; } = null!;

        /// <summary>
        /// Region name
        /// </summary>
        [Required, MaxLength(64)]
        public string Region { get; set; } = string.Empty;

        /// <summary>
        /// Total usage count in this region
        /// </summary>
        public long TotalUsageCount { get; set; }

        /// <summary>
        /// Percentage of recipes in this region using this ingredient
        /// </summary>
        public decimal PenetrationRate { get; set; }

        /// <summary>
        /// Average quantity used per recipe
        /// </summary>
        public decimal? AverageQuantity { get; set; }

        /// <summary>
        /// Most common unit in this region
        /// </summary>
        [MaxLength(32)]
        public string? CommonUnit { get; set; }

        /// <summary>
        /// Popularity rank within region (1 = most popular)
        /// </summary>
        public int PopularityRank { get; set; }

        /// <summary>
        /// Seasonal peak months (e.g., "11,12,1" for Nov-Jan)
        /// </summary>
        [MaxLength(32)]
        public string? SeasonalPeakMonths { get; set; }

        /// <summary>
        /// Most common pairings in this region (comma-separated GlobalIngredient IDs)
        /// </summary>
        [MaxLength(512)]
        public string? CommonPairings { get; set; }

        /// <summary>
        /// Common dietary contexts (e.g., "vegan,keto")
        /// </summary>
        [MaxLength(256)]
        public string? CommonDietaryContexts { get; set; }

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Tracks ingredient pairings (what goes with what).
    /// High-value B2B data for product development.
    /// </summary>
    [Index(nameof(IngredientAId), nameof(IngredientBId))]
    public class IngredientPairing
    {
        public int Id { get; set; }

        public Guid IngredientAId { get; set; }
        public GlobalIngredient IngredientA { get; set; } = null!;

        public Guid IngredientBId { get; set; }
        public GlobalIngredient IngredientB { get; set; } = null!;

        /// <summary>
        /// Number of recipes containing both ingredients
        /// </summary>
        public int CoOccurrenceCount { get; set; }

        /// <summary>
        /// Affinity score (0-100): how often they appear together vs separately
        /// </summary>
        public decimal AffinityScore { get; set; }

        /// <summary>
        /// Region where this pairing is most common (null = global)
        /// </summary>
        [MaxLength(64)]
        public string? TopRegion { get; set; }

        /// <summary>
        /// Common cuisine types where this pairing appears
        /// </summary>
        [MaxLength(256)]
        public string? CommonCuisines { get; set; }

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Dietary trend tracking for B2B market insights.
    /// </summary>
    [Index(nameof(DietaryTag), nameof(Date))]
    public class DietaryTrend
    {
        public long Id { get; set; }

        /// <summary>
        /// The dietary tag (e.g., "vegan", "keto", "halal")
        /// </summary>
        [Required, MaxLength(64)]
        public string DietaryTag { get; set; } = string.Empty;

        public DateOnly Date { get; set; }

        /// <summary>
        /// Region (null = global)
        /// </summary>
        [MaxLength(64)]
        public string? Region { get; set; }

        /// <summary>
        /// Number of recipes created with this tag
        /// </summary>
        public int RecipeCount { get; set; }

        /// <summary>
        /// Number of unique users creating recipes with this tag
        /// </summary>
        public int UniqueUsers { get; set; }

        /// <summary>
        /// Percentage growth from previous period
        /// </summary>
        public decimal? GrowthRate { get; set; }

        /// <summary>
        /// Market share percentage among all recipes
        /// </summary>
        public decimal MarketShare { get; set; }

        public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Tracks search queries for ingredient demand signals.
    /// </summary>
    [Index(nameof(Query), nameof(CreatedAt))]
    public class IngredientSearchLog
    {
        public long Id { get; set; }

        /// <summary>
        /// The search query
        /// </summary>
        [Required, MaxLength(256)]
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// Matched GlobalIngredient (if found)
        /// </summary>
        public Guid? MatchedIngredientId { get; set; }

        /// <summary>
        /// Whether a match was found
        /// </summary>
        public bool WasFound { get; set; }

        /// <summary>
        /// User who searched (null = anonymous)
        /// </summary>
        public Guid? UserId { get; set; }

        /// <summary>
        /// Region of the searcher
        /// </summary>
        [MaxLength(64)]
        public string? Region { get; set; }

        /// <summary>
        /// Context of search (e.g., "recipe_create", "substitution", "browse")
        /// </summary>
        [MaxLength(32)]
        public string? SearchContext { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
