using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    public class GlobalIngredientAllergen
    {
        public Guid GlobalIngredientId { get; set; }
        public GlobalIngredient GlobalIngredient { get; set; } = null!;

        public string AllergenType { get; set; } = string.Empty;

        // ✅ Store enum in DB int column "SeverityLevel"
        [Column("SeverityLevel")]
        public AllergenSeverity Severity { get; set; } = AllergenSeverity.Low;

        public bool MayContain { get; set; } = false;
        public bool CrossContactRisk { get; set; } = false;
        public bool ProcessingAid { get; set; } = false;

        // Optional metadata
        public string? ENumber { get; set; }
        public string? Notes { get; set; }

        // ✅ DB has a varchar column named "Severity" — IGNORE it (we rely on SeverityLevel)
        [NotMapped]
        public string? SeverityText { get; set; }
    }
}
