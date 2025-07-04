namespace Platerly.Api.DTOs
{
    public class CreateRecipeDto
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string? ImageUrl { get; set; }

        public List<IngredientDto> Ingredients { get; set; } = new();
    }
}
