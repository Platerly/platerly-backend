using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Platerly.Api.Models
{
    [Index(nameof(NormalizedAlias))]
    public class GlobalIngredientAlias
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid GlobalIngredientId { get; set; }
        public GlobalIngredient GlobalIngredient { get; set; } = null!;

        /// <summary>Single alias, as originally written (e.g. "Courgette").</summary>
        [Required, MaxLength(256)]
        public string Alias { get; set; } = string.Empty;

        /// <summary>Lowercase trimmed form used for fast SQL matching.</summary>
        [Required, MaxLength(256)]
        public string NormalizedAlias { get; set; } = string.Empty;
    }
}
