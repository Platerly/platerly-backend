using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.DTOs
{
    public class UpdateRecipeDto
    {
        [StringLength(160, MinimumLength = 1, ErrorMessage = "Title must be between 1 and 160 characters")]
        public string? Title { get; set; }

        [StringLength(4000, ErrorMessage = "Description cannot exceed 4000 characters")]
        public string? Description { get; set; }

        [StringLength(8000, ErrorMessage = "Instructions cannot exceed 8000 characters")]
        public string? Instructions { get; set; }

        [StringLength(64, ErrorMessage = "Region cannot exceed 64 characters")]
        public string? Region { get; set; }

        [StringLength(500, ErrorMessage = "Tags cannot exceed 500 characters")]
        public string? Tags { get; set; }

        [Range(0, 10000, ErrorMessage = "Prep time must be between 0 and 10000 minutes")]
        public int? PrepTimeMinutes { get; set; }

        [Range(0, 10000, ErrorMessage = "Cook time must be between 0 and 10000 minutes")]
        public int? CookTimeMinutes { get; set; }

        [StringLength(16, ErrorMessage = "Difficulty cannot exceed 16 characters")]
        public string? Difficulty { get; set; }

        // optional new image; send this request as multipart/form-data
        public IFormFile? Image { get; set; }
    }
}
