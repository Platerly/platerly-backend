namespace Platerly.Api.DTOs
{
    public class IngredientDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Quantity { get; set; }

        // NEW (optional for highlighting)
        public bool? IsAllergen { get; set; }       // null/false if alerts off
        public string? AllergySeverity { get; set; } // "high" | "trace" | null
    }
}
