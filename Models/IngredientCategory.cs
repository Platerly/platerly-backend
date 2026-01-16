using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Hierarchical taxonomy for ingredients.
    /// Enables grouping, searching, and substitution logic.
    /// Example: Dairy > Cheese > Hard Cheese > Parmesan
    /// </summary>
    public class IngredientCategory
    {
        public int Id { get; set; }

        [Required, MaxLength(128)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// URL-friendly slug for the category
        /// </summary>
        [Required, MaxLength(128)]
        public string Slug { get; set; } = string.Empty;

        /// <summary>
        /// Description of this category
        /// </summary>
        [MaxLength(512)]
        public string? Description { get; set; }

        /// <summary>
        /// Parent category ID for hierarchy (null = root category)
        /// </summary>
        public int? ParentCategoryId { get; set; }
        public IngredientCategory? ParentCategory { get; set; }

        /// <summary>
        /// Child categories
        /// </summary>
        public ICollection<IngredientCategory> ChildCategories { get; set; } = new List<IngredientCategory>();

        /// <summary>
        /// GlobalIngredients in this category
        /// </summary>
        public ICollection<GlobalIngredient> Ingredients { get; set; } = new List<GlobalIngredient>();

        /// <summary>
        /// Icon name for UI display
        /// </summary>
        [MaxLength(64)]
        public string? IconName { get; set; }

        /// <summary>
        /// Sort order within parent
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Whether this category is active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Common allergens associated with this category
        /// </summary>
        [MaxLength(256)]
        public string? CommonAllergens { get; set; }

        /// <summary>
        /// Dietary flags for the category (e.g., "vegan,gluten-free")
        /// </summary>
        [MaxLength(256)]
        public string? DietaryFlags { get; set; }
    }
}
