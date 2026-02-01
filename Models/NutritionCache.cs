using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Caches USDA nutrition data per ingredient to avoid repeated API calls.
    /// </summary>
    public class NutritionCache
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid GlobalIngredientId { get; set; }

        [ForeignKey(nameof(GlobalIngredientId))]
        public GlobalIngredient? GlobalIngredient { get; set; }

        /// <summary>
        /// USDA FoodData Central ID
        /// </summary>
        public int? UsdaFdcId { get; set; }

        /// <summary>
        /// USDA food description used for the match
        /// </summary>
        [MaxLength(512)]
        public string? UsdaDescription { get; set; }

        /// <summary>
        /// All values per 100g
        /// </summary>
        public decimal CaloriesPer100g { get; set; }
        public decimal ProteinPer100g { get; set; }
        public decimal CarbsPer100g { get; set; }
        public decimal FatPer100g { get; set; }
        public decimal FiberPer100g { get; set; }
        public decimal SodiumMgPer100g { get; set; }
        public decimal SugarPer100g { get; set; }
        public decimal SaturatedFatPer100g { get; set; }
        public decimal CholesterolMgPer100g { get; set; }

        /// <summary>
        /// Confidence of the ingredient-to-USDA match (0-100)
        /// </summary>
        public int MatchConfidence { get; set; } = 50;

        public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
