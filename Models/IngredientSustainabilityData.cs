using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    public class IngredientSustainabilityData
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid GlobalIngredientId { get; set; }

        [ForeignKey(nameof(GlobalIngredientId))]
        public GlobalIngredient? GlobalIngredient { get; set; }

        /// <summary>
        /// Estimated CO2 equivalent per 100g (kg CO2e)
        /// </summary>
        public decimal CarbonScorePer100g { get; set; }

        /// <summary>
        /// Estimated water usage per 100g (liters)
        /// </summary>
        public decimal WaterUsagePer100g { get; set; }

        /// <summary>
        /// Months when this ingredient is in season (comma-separated: "1,2,3" for Jan-Mar)
        /// </summary>
        [MaxLength(64)]
        public string? SeasonMonths { get; set; }

        /// <summary>
        /// Transport impact score 0-100 (higher = more transport impact)
        /// </summary>
        public int TransportScore { get; set; } = 50;

        /// <summary>
        /// Overall sustainability score 0-100 (higher = more sustainable)
        /// </summary>
        public int OverallScore { get; set; } = 50;

        [MaxLength(256)]
        public string? DataSource { get; set; }

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
