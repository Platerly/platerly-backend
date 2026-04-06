using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Platerly.Api.Models
{
    [Index(nameof(Name))]
    [Index(nameof(NormalizedName))]
    [Index(nameof(CategoryId))]
    public class GlobalIngredient
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(256)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Lowercase, trimmed version for matching
        /// </summary>
        [MaxLength(256)]
        public string NormalizedName { get; set; } = string.Empty;

        /// <summary>
        /// Alternative names/spellings (comma-separated).
        /// DEPRECATED — use <see cref="IngredientAliases"/> (GlobalIngredientAliases table) instead.
        /// Kept for reference during migration; do not write new data here.
        /// </summary>
        [MaxLength(512)]
        public string? Aliases { get; set; }

        /// <summary>
        /// Normalised alias rows migrated from <see cref="Aliases"/> into the GlobalIngredientAliases table.
        /// </summary>
        public List<GlobalIngredientAlias> IngredientAliases { get; set; } = new();

        // Allergen tracking
        public bool IsAllergenic { get; set; } = false;
        public List<GlobalIngredientAllergen> Allergens { get; set; } = new();

        // Category/taxonomy
        public int? CategoryId { get; set; }
        public IngredientCategory? Category { get; set; }

        // Recipe ingredient instances
        public List<Ingredient> Ingredients { get; set; } = new();

        // Substitution relationships
        public ICollection<IngredientSubstitution> SubstitutionsAsOriginal { get; set; } = new List<IngredientSubstitution>();
        public ICollection<IngredientSubstitution> SubstitutionsAsSubstitute { get; set; } = new List<IngredientSubstitution>();

        // ---- B2B Analytics Fields ----

        /// <summary>
        /// Total times this ingredient has been used across all recipes
        /// </summary>
        public long TotalUsageCount { get; set; } = 0;

        /// <summary>
        /// Number of unique recipes using this ingredient
        /// </summary>
        public int UniqueRecipeCount { get; set; } = 0;

        /// <summary>
        /// Global popularity rank (1 = most popular)
        /// </summary>
        public int? PopularityRank { get; set; }

        /// <summary>
        /// Average rating of recipes containing this ingredient
        /// </summary>
        public decimal? AverageRecipeRating { get; set; }

        /// <summary>
        /// Primary region where this ingredient is used
        /// </summary>
        [MaxLength(64)]
        public string? PrimaryRegion { get; set; }

        /// <summary>
        /// Dietary flags (e.g., "vegan,gluten-free,halal")
        /// </summary>
        [MaxLength(256)]
        public string? DietaryFlags { get; set; }

        /// <summary>
        /// Nutritional category (protein, carb, fat, vegetable, etc.)
        /// </summary>
        [MaxLength(64)]
        public string? NutritionalCategory { get; set; }

        /// <summary>
        /// Whether this ingredient is verified/curated by admin
        /// </summary>
        public bool IsVerified { get; set; } = false;

        /// <summary>
        /// Whether this ingredient is active (false = soft deleted/merged)
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// If merged, points to the canonical ingredient
        /// </summary>
        public Guid? MergedIntoId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }

        // ---- Nickel Data (sourced from Rebelytics R&D Inc. v9.1.1, September 2025) ----

        /// <summary>
        /// Mean nickel content in micrograms per serving. Null = not yet measured.
        /// Source: Rebelytics R&amp;D Inc., Nickel in Foods Summary Tables v9.1.1, September 2025.
        /// Compiled from 257 international food composition studies, 20,575 individual measurements.
        /// </summary>
        [Column(TypeName = "decimal(10,4)")]
        public decimal? NickelMicrogramsPerServing { get; set; }

        /// <summary>
        /// Serving size in grams used for the nickel measurement.
        /// </summary>
        public int? NickelServingSizeGrams { get; set; }

        /// <summary>
        /// Nickel classification per clinical threshold (LOW &lt;10 μg, MODERATE 10–20 μg, HIGH &gt;20 μg per serving).
        /// </summary>
        public NickelClassification NickelClassification { get; set; } = NickelClassification.Unknown;

        /// <summary>
        /// Documented source of the nickel data. Never null for verified entries.
        /// </summary>
        [MaxLength(512)]
        public string? NickelDataSource { get; set; }

        /// <summary>
        /// Year the source data was published or compiled.
        /// </summary>
        public int? NickelDataSourceYear { get; set; }

        /// <summary>
        /// Number of individual measurements the mean is based on. Used to derive confidence level.
        /// </summary>
        public int? NickelDataPointCount { get; set; }

        /// <summary>
        /// Standard deviation of nickel measurements in micrograms. High SD indicates high variability.
        /// </summary>
        [Column(TypeName = "decimal(10,4)")]
        public decimal? NickelStandardDeviation { get; set; }

        /// <summary>
        /// Confidence level derived from data point count.
        /// HIGH: 10+ points, MEDIUM: 3–9 points, LOW: 1–2 points, UNKNOWN: no data.
        /// </summary>
        public NickelConfidenceLevel NickelConfidenceLevel { get; set; } = NickelConfidenceLevel.Unknown;

        /// <summary>
        /// True if the standard deviation is exceptionally high relative to the mean,
        /// indicating wide variability between samples (e.g., spinach).
        /// </summary>
        public bool NickelHighVariability { get; set; } = false;

        /// <summary>
        /// Optional preparation note affecting nickel content (e.g., draining canned beans reduces nickel).
        /// </summary>
        [MaxLength(512)]
        public string? NickelPreparationNote { get; set; }
    }
}
