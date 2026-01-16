using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    /// <summary>
    /// A meal plan for organizing recipes across days/meals.
    /// Can generate a consolidated shopping list.
    /// </summary>
    public class MealPlan
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        /// <summary>
        /// Name of the meal plan (e.g., "Week of Jan 13", "Thanksgiving Menu")
        /// </summary>
        [MaxLength(128)]
        public string Name { get; set; } = "My Meal Plan";

        /// <summary>
        /// Start date of the meal plan
        /// </summary>
        public DateTime StartDate { get; set; }

        /// <summary>
        /// End date of the meal plan
        /// </summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Optional notes
        /// </summary>
        [MaxLength(1024)]
        public string? Notes { get; set; }

        /// <summary>
        /// Unique token for sharing
        /// </summary>
        [MaxLength(64)]
        public string? ShareToken { get; set; }

        public bool IsShared { get; set; } = false;

        /// <summary>
        /// Meals in this plan
        /// </summary>
        public ICollection<MealPlanEntry> Entries { get; set; } = new List<MealPlanEntry>();

        /// <summary>
        /// Shopping lists generated from this meal plan
        /// </summary>
        public ICollection<ShoppingList> ShoppingLists { get; set; } = new List<ShoppingList>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
    }

    /// <summary>
    /// A single meal entry in a meal plan (e.g., "Monday Dinner: Chicken Curry")
    /// </summary>
    public class MealPlanEntry
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid MealPlanId { get; set; }

        [ForeignKey(nameof(MealPlanId))]
        public MealPlan? MealPlan { get; set; }

        public Guid RecipeId { get; set; }

        [ForeignKey(nameof(RecipeId))]
        public Recipe? Recipe { get; set; }

        /// <summary>
        /// Date for this meal
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Meal type
        /// </summary>
        public MealType MealType { get; set; } = MealType.Dinner;

        /// <summary>
        /// Scale factor (e.g., 2.0 to double the recipe)
        /// </summary>
        public decimal ScaleFactor { get; set; } = 1.0m;

        /// <summary>
        /// Override servings
        /// </summary>
        public int? Servings { get; set; }

        /// <summary>
        /// Notes for this specific meal
        /// </summary>
        [MaxLength(256)]
        public string? Notes { get; set; }

        /// <summary>
        /// Sort order for multiple recipes in same meal slot
        /// </summary>
        public int SortOrder { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    public enum MealType
    {
        Breakfast = 0,
        Lunch = 1,
        Dinner = 2,
        Snack = 3,
        Dessert = 4,
        Other = 5
    }
}
