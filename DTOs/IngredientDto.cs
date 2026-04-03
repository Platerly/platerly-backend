using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.DTOs
{
    public class IngredientDto
    {
        // NEW: stable row id of the Ingredient (Guid in most setups; change to int if needed)
        public Guid IngredientId { get; set; }

        // NEW: normalized lookup id (GlobalIngredients.Id)
        public Guid? GlobalIngredientId { get; set; }

        [Required(ErrorMessage = "Ingredient name is required")]
        [MaxLength(256, ErrorMessage = "Ingredient name cannot exceed 256 characters")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(64, ErrorMessage = "Quantity cannot exceed 64 characters")]
        public string? Quantity { get; set; }

        // keep your highlighting fields
        public bool? IsAllergen { get; set; }        // null/false if alerts off
        public string? AllergySeverity { get; set; } // "high" | "medium" | "low" | null
    }
}
