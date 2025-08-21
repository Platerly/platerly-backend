namespace Platerly.Api.DTOs
{
    public class UpdateRecipeDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Instructions { get; set; }
        public string? Region { get; set; }
        public string? Tags { get; set; }
        public int? PrepTimeMinutes { get; set; }
        public int? CookTimeMinutes { get; set; }
        public string? Difficulty { get; set; }

        // optional new image; send this request as multipart/form-data
        public IFormFile? Image { get; set; }
    }
}
