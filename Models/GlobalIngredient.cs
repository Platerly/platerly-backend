using System.ComponentModel.DataAnnotations;
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
        /// Alternative names/spellings (comma-separated)
        /// e.g., "courgette,zuchinni" for "zucchini"
        /// </summary>
        [MaxLength(512)]
        public string? Aliases { get; set; }

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
    }
}
