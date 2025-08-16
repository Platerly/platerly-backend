using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    public class Ingredient
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string? Quantity { get; set; }

        public Guid RecipeId { get; set; }
        public Recipe? Recipe { get; set; }

        // Link to the normalized global ingredient
        public Guid GlobalIngredientId { get; set; }
        public GlobalIngredient? GlobalIngredient { get; set; }

        public bool IsAllergenic { get; set; } = false;

        // 🔧 Virtual name property for compatibility
        [NotMapped]
        public string Name => GlobalIngredient?.Name ?? string.Empty;
        public List<IngredientAllergen> Allergens { get; set; } = new();

    }
}
