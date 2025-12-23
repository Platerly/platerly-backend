using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.Models
{
    public class AllergenKeyword
    {
        public int Id { get; set; }

        [Required, MaxLength(128)]
        public string Text { get; set; } = null!;  // stored as case-insensitive

        public AllergenKeywordKind Kind { get; set; }

        // Optional helpers
        [MaxLength(16)]
        public string? ENumber { get; set; }   // if Kind == ENumber

        [MaxLength(256)]
        public string? Notes { get; set; }

        public ICollection<AllergenKeywordAllergy> Allergies { get; set; } = new List<AllergenKeywordAllergy>();
    }
}
