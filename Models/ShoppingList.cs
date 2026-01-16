using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    /// <summary>
    /// A persistent shopping list that can aggregate ingredients from multiple recipes.
    /// Supports sharing via unique share tokens.
    /// </summary>
    public class ShoppingList
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Owner of the shopping list
        /// </summary>
        public Guid UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        /// <summary>
        /// Custom name for the list (e.g., "Weekly Groceries", "Thanksgiving Dinner")
        /// </summary>
        [MaxLength(128)]
        public string Name { get; set; } = "My Shopping List";

        /// <summary>
        /// Optional notes for the list
        /// </summary>
        [MaxLength(512)]
        public string? Notes { get; set; }

        /// <summary>
        /// Unique token for sharing (null = not shared)
        /// </summary>
        [MaxLength(64)]
        public string? ShareToken { get; set; }

        /// <summary>
        /// Whether the list is publicly accessible via share link
        /// </summary>
        public bool IsShared { get; set; } = false;

        /// <summary>
        /// Whether shared users can edit the list
        /// </summary>
        public bool AllowCollaborativeEdit { get; set; } = false;

        /// <summary>
        /// Linked to a meal plan (optional)
        /// </summary>
        public Guid? MealPlanId { get; set; }

        [ForeignKey(nameof(MealPlanId))]
        public MealPlan? MealPlan { get; set; }

        /// <summary>
        /// Items in the shopping list
        /// </summary>
        public ICollection<ShoppingListItem> Items { get; set; } = new List<ShoppingListItem>();

        /// <summary>
        /// Recipes that contributed to this shopping list
        /// </summary>
        public ICollection<ShoppingListRecipe> SourceRecipes { get; set; } = new List<ShoppingListRecipe>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Soft delete
        /// </summary>
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
    }

    /// <summary>
    /// An individual item in a shopping list.
    /// Supports smart quantity consolidation from multiple recipes.
    /// </summary>
    public class ShoppingListItem
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ShoppingListId { get; set; }

        [ForeignKey(nameof(ShoppingListId))]
        public ShoppingList? ShoppingList { get; set; }

        /// <summary>
        /// Link to global ingredient for normalization (optional)
        /// </summary>
        public Guid? GlobalIngredientId { get; set; }

        [ForeignKey(nameof(GlobalIngredientId))]
        public GlobalIngredient? GlobalIngredient { get; set; }

        /// <summary>
        /// Display name (may differ from GlobalIngredient for custom items)
        /// </summary>
        [Required]
        [MaxLength(256)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Consolidated quantity (e.g., "3 cups", "500g")
        /// </summary>
        [MaxLength(64)]
        public string? Quantity { get; set; }

        /// <summary>
        /// Parsed numeric amount for consolidation (e.g., 3.0 for "3 cups")
        /// </summary>
        public decimal? NumericAmount { get; set; }

        /// <summary>
        /// Unit of measurement (e.g., "cups", "g", "oz", "pieces")
        /// </summary>
        [MaxLength(32)]
        public string? Unit { get; set; }

        /// <summary>
        /// Category for grouping in store (e.g., "Produce", "Dairy", "Meat")
        /// </summary>
        [MaxLength(64)]
        public string? Category { get; set; }

        /// <summary>
        /// Aisle/location hint for the store
        /// </summary>
        [MaxLength(64)]
        public string? AisleHint { get; set; }

        /// <summary>
        /// Whether this item has been checked off
        /// </summary>
        public bool IsChecked { get; set; } = false;

        /// <summary>
        /// When the item was checked off
        /// </summary>
        public DateTime? CheckedAt { get; set; }

        /// <summary>
        /// Custom note for this item (e.g., "get organic")
        /// </summary>
        [MaxLength(256)]
        public string? Note { get; set; }

        /// <summary>
        /// Sort order within the list
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Whether this is a manually added item (not from a recipe)
        /// </summary>
        public bool IsManuallyAdded { get; set; } = false;

        /// <summary>
        /// Source breakdown - which recipes contributed to this item
        /// </summary>
        public ICollection<ShoppingListItemSource> Sources { get; set; } = new List<ShoppingListItemSource>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Tracks which recipes contributed to a shopping list item.
    /// Enables "3 cups flour: 2 cups from Recipe A, 1 cup from Recipe B"
    /// </summary>
    public class ShoppingListItemSource
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ShoppingListItemId { get; set; }

        [ForeignKey(nameof(ShoppingListItemId))]
        public ShoppingListItem? ShoppingListItem { get; set; }

        public Guid RecipeId { get; set; }

        [ForeignKey(nameof(RecipeId))]
        public Recipe? Recipe { get; set; }

        /// <summary>
        /// Original quantity from this recipe
        /// </summary>
        [MaxLength(64)]
        public string? OriginalQuantity { get; set; }

        /// <summary>
        /// Scale factor applied (e.g., 2.0 for doubled recipe)
        /// </summary>
        public decimal ScaleFactor { get; set; } = 1.0m;

        /// <summary>
        /// Parsed numeric amount from this source
        /// </summary>
        public decimal? NumericAmount { get; set; }

        [MaxLength(32)]
        public string? Unit { get; set; }
    }

    /// <summary>
    /// Junction table for recipes in a shopping list
    /// </summary>
    public class ShoppingListRecipe
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ShoppingListId { get; set; }

        [ForeignKey(nameof(ShoppingListId))]
        public ShoppingList? ShoppingList { get; set; }

        public Guid RecipeId { get; set; }

        [ForeignKey(nameof(RecipeId))]
        public Recipe? Recipe { get; set; }

        /// <summary>
        /// Scale factor for this recipe's ingredients
        /// </summary>
        public decimal ScaleFactor { get; set; } = 1.0m;

        /// <summary>
        /// Number of servings (overrides recipe default if set)
        /// </summary>
        public int? Servings { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
