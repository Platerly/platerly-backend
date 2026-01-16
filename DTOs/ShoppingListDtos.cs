using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.DTOs
{
    // ========== Shopping List DTOs ==========

    public class ShoppingListDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public bool IsShared { get; set; }
        public string? ShareUrl { get; set; }
        public bool AllowCollaborativeEdit { get; set; }
        public Guid? MealPlanId { get; set; }
        public string? MealPlanName { get; set; }
        public List<ShoppingListItemDto> Items { get; set; } = new();
        public List<ShoppingListRecipeDto> SourceRecipes { get; set; } = new();
        public int TotalItems { get; set; }
        public int CheckedItems { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class ShoppingListSummaryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalItems { get; set; }
        public int CheckedItems { get; set; }
        public int RecipeCount { get; set; }
        public List<Guid> RecipeIds { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class ShoppingListItemDto
    {
        public Guid Id { get; set; }
        public Guid? GlobalIngredientId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Quantity { get; set; }
        public decimal? NumericAmount { get; set; }
        public string? Unit { get; set; }
        public string? Category { get; set; }
        public string? AisleHint { get; set; }
        public bool IsChecked { get; set; }
        public DateTime? CheckedAt { get; set; }
        public string? Note { get; set; }
        public int SortOrder { get; set; }
        public bool IsManuallyAdded { get; set; }
        public List<ItemSourceDto> Sources { get; set; } = new();
    }

    public class ItemSourceDto
    {
        public Guid RecipeId { get; set; }
        public string RecipeTitle { get; set; } = string.Empty;
        public string? OriginalQuantity { get; set; }
        public decimal ScaleFactor { get; set; }
    }

    public class ShoppingListRecipeDto
    {
        public Guid RecipeId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public decimal ScaleFactor { get; set; }
        public int? Servings { get; set; }
        public DateTime AddedAt { get; set; }
    }

    // ========== Create/Update DTOs ==========

    public class CreateShoppingListDto
    {
        [MaxLength(128)]
        public string Name { get; set; } = "My Shopping List";

        [MaxLength(512)]
        public string? Notes { get; set; }

        /// <summary>
        /// Optional: create from a recipe immediately
        /// </summary>
        public Guid? RecipeId { get; set; }

        /// <summary>
        /// Scale factor for initial recipe
        /// </summary>
        public decimal ScaleFactor { get; set; } = 1.0m;
    }

    public class UpdateShoppingListDto
    {
        [MaxLength(128)]
        public string? Name { get; set; }

        [MaxLength(512)]
        public string? Notes { get; set; }
    }

    public class AddRecipeToListDto
    {
        [Required]
        public Guid RecipeId { get; set; }

        public decimal ScaleFactor { get; set; } = 1.0m;

        public int? Servings { get; set; }
    }

    public class AddManualItemDto
    {
        [Required, MaxLength(256)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? Quantity { get; set; }

        [MaxLength(64)]
        public string? Category { get; set; }

        [MaxLength(256)]
        public string? Note { get; set; }
    }

    public class UpdateItemDto
    {
        public bool? IsChecked { get; set; }

        [MaxLength(64)]
        public string? Quantity { get; set; }

        [MaxLength(256)]
        public string? Note { get; set; }

        public int? SortOrder { get; set; }
    }

    public class BulkCheckItemsDto
    {
        public List<Guid> ItemIds { get; set; } = new();
        public bool IsChecked { get; set; }
    }

    public class ShareListDto
    {
        public bool IsShared { get; set; }
        public bool AllowCollaborativeEdit { get; set; }
    }

    public class ShareListResultDto
    {
        public bool IsShared { get; set; }
        public string? ShareUrl { get; set; }
        public string? ShareToken { get; set; }
    }

    // ========== Meal Plan DTOs ==========

    public class MealPlanDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? Notes { get; set; }
        public bool IsShared { get; set; }
        public string? ShareUrl { get; set; }
        public List<MealPlanEntryDto> Entries { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class MealPlanSummaryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int TotalMeals { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class MealPlanEntryDto
    {
        public Guid Id { get; set; }
        public Guid RecipeId { get; set; }
        public string RecipeTitle { get; set; } = string.Empty;
        public string? RecipeThumbnail { get; set; }
        public DateTime Date { get; set; }
        public string MealType { get; set; } = string.Empty;
        public decimal ScaleFactor { get; set; }
        public int? Servings { get; set; }
        public string? Notes { get; set; }
    }

    public class CreateMealPlanDto
    {
        [MaxLength(128)]
        public string Name { get; set; } = "My Meal Plan";

        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        [MaxLength(1024)]
        public string? Notes { get; set; }
    }

    public class AddMealPlanEntryDto
    {
        [Required]
        public Guid RecipeId { get; set; }

        [Required]
        public DateTime Date { get; set; }

        public string MealType { get; set; } = "Dinner";

        public decimal ScaleFactor { get; set; } = 1.0m;

        public int? Servings { get; set; }

        [MaxLength(256)]
        public string? Notes { get; set; }
    }

    public class GenerateShoppingListFromMealPlanDto
    {
        [MaxLength(128)]
        public string? ListName { get; set; }

        /// <summary>
        /// If true, consolidate same ingredients across recipes
        /// </summary>
        public bool ConsolidateIngredients { get; set; } = true;
    }
}
