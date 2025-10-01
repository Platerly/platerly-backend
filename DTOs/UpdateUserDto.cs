namespace Platerly.Api.DTOs
{
    public class UpdateUserDto
    {
        public string? DisplayName { get; set; }
        public string? Bio { get; set; }
        public string? ProfileImageUrl { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? YouTubeUrl { get; set; }
        public string? XUrl { get; set; }
        public string? InstagramUrl { get; set; }

        public List<string>? AllergyKeywords { get; set; }
    }
}
