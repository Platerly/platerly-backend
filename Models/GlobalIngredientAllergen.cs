namespace Platerly.Api.Models
{
    public class GlobalIngredientAllergen
    {
        public Guid GlobalIngredientId { get; set; }
        public GlobalIngredient GlobalIngredient { get; set; } = null!;

        public string AllergenType { get; set; } = string.Empty;

        
        public AllergenSeverity Severity { get; set; } = AllergenSeverity.Low;
        public bool MayContain { get; set; } = false;
        public bool CrossContactRisk { get; set; } = false;
        public bool ProcessingAid { get; set; } = false;

        // Optional metadata (e.g., E220–E228 for sulfites)
        public string? ENumber { get; set; }
        public string? Notes { get; set; }
    }

}
