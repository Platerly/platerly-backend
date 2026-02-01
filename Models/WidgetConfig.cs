using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Platerly.Api.Models
{
    public class WidgetConfig
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ApiKeyId { get; set; }

        [ForeignKey(nameof(ApiKeyId))]
        public ApiKey? ApiKey { get; set; }

        /// <summary>
        /// JSON theme configuration (colors, fonts, etc.)
        /// </summary>
        [MaxLength(4096)]
        public string? ThemeJson { get; set; }

        public int MaxRecipes { get; set; } = 10;

        /// <summary>
        /// Comma-separated dietary filters
        /// </summary>
        [MaxLength(256)]
        public string? DietaryFilters { get; set; }

        [MaxLength(64)]
        public string? RegionFilter { get; set; }

        [MaxLength(512)]
        public string? LogoUrl { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
