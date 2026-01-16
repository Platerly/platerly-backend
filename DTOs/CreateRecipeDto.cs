using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.DTOs
{
    public class CreateRecipeDto
    {
        [Required(ErrorMessage = "Title is required")]
        [StringLength(160, MinimumLength = 1, ErrorMessage = "Title must be between 1 and 160 characters")]
        public string Title { get; set; } = string.Empty;

        [StringLength(4000, ErrorMessage = "Description cannot exceed 4000 characters")]
        public string Description { get; set; } = string.Empty;

        [StringLength(8000, ErrorMessage = "Instructions cannot exceed 8000 characters")]
        public string Instructions { get; set; } = string.Empty;

        [StringLength(64, ErrorMessage = "Region cannot exceed 64 characters")]
        public string? Region { get; set; }

        [StringLength(1024, ErrorMessage = "Image URL cannot exceed 1024 characters")]
        public string? ImageUrl { get; set; }

        [StringLength(256, ErrorMessage = "Image blob name cannot exceed 256 characters")]
        public string? ImageBlobName { get; set; }

        public List<string> Tags { get; set; } = new();

        [Range(0, 10000, ErrorMessage = "Prep time must be between 0 and 10000 minutes")]
        public int PrepTimeMinutes { get; set; } = 0;

        [Range(0, 10000, ErrorMessage = "Cook time must be between 0 and 10000 minutes")]
        public int CookTimeMinutes { get; set; } = 0;

        [StringLength(16, ErrorMessage = "Difficulty cannot exceed 16 characters")]
        public string Difficulty { get; set; } = string.Empty;

        [StringLength(16, ErrorMessage = "Type cannot exceed 16 characters")]
        public string Type { get; set; } = "Meal"; // Meal, Shake, Dessert, Snack, Appetizer, Side, Beverage

        public List<IngredientDto> Ingredients { get; set; } = new();
    }
}
