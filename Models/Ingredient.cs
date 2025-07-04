using System;

namespace Platerly.Api.Models
{
    public class Ingredient
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public string? Quantity { get; set; } // e.g., "2 cups", "1 tbsp", etc.

        // Foreign Key
        public Guid RecipeId { get; set; }

        // Navigation property
        public Recipe? Recipe { get; set; }

        // Allergy tagging
        public bool IsAllergenic { get; set; } = false;

        // List of tags like {"nickel", "soy", "milk"}
        public List<string> AllergenTypes { get; set; } = new List<string>();
    }
}
