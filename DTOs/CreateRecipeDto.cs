namespace Platerly.Api.DTOs
{
    public class CreateRecipeDto
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string? ImageUrl { get; set; }
        public string? ImageBlobName { get; set; } // Blob storage name for the image
        public List<string> Tags { get; set; } = new(); // Comma-separated tags, e.g., "vegan, low-nickel"

        public int PrepTimeMinutes { get; set; } = 0; // Preparation time in minutes
        public int CookTimeMinutes { get; set; } = 0; // Cooking time in minutes
        public string Difficulty { get; set; } = string.Empty; // e.g., "Easy", "Medium", "Hard"

        public List<IngredientDto> Ingredients { get; set; } = new();
    }
}
