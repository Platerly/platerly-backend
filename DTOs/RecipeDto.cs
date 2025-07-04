namespace Platerly.Api.DTOs
{
    public class RecipeDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Instructions { get; set; }
        public string? Region { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<IngredientDto> Ingredients { get; set; } = new();
        public List<string> IngredientNames { get; set; } = new();
        public string AuthorUsername { get; set; } = string.Empty;
    }
}
