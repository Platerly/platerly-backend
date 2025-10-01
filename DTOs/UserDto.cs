namespace Platerly.Api.DTOs
{
    public class UserDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Bio { get; set; }
        public string? ProfileImageUrl { get; set; }
        public bool IsEmailVerified { get; set; }
        public DateTime CreatedAt { get; set; }

        // NEW: profile links (used by settings/edit form)
        public string? WebsiteUrl { get; set; }
        public string? YouTubeUrl { get; set; }
        public string? XUrl { get; set; }
        public string? InstagramUrl { get; set; }

        // Optional: handy if you also want to show/edit banner in settings
        public string? BannerImageUrl { get; set; }

    }
}
