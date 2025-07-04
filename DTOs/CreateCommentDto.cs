namespace Platerly.Api.DTOs
{
    public class CreateCommentDto
    {
        public Guid RecipeId { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}
