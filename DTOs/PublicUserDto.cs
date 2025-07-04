namespace Platerly.Api.DTOs
{
    public class PublicUserDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Bio { get; set; }
        public string? ProfileImageUrl { get; set; }
        public DateTime CreatedAt { get; set; }

        // Future:
        // public int PublicCookbookCount { get; set; }
        // public int RecipeCount { get; set; }
    }
}
