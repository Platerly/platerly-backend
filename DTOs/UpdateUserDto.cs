using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.DTOs
{
    public class UpdateUserDto
    {
        [StringLength(128, ErrorMessage = "Display name cannot exceed 128 characters")]
        public string? DisplayName { get; set; }

        [StringLength(1024, ErrorMessage = "Bio cannot exceed 1024 characters")]
        public string? Bio { get; set; }

        [StringLength(1024, ErrorMessage = "Profile image URL cannot exceed 1024 characters")]
        public string? ProfileImageUrl { get; set; }

        [StringLength(512, ErrorMessage = "Website URL cannot exceed 512 characters")]
        public string? WebsiteUrl { get; set; }

        [StringLength(512, ErrorMessage = "YouTube URL cannot exceed 512 characters")]
        public string? YouTubeUrl { get; set; }

        [StringLength(512, ErrorMessage = "X URL cannot exceed 512 characters")]
        public string? XUrl { get; set; }

        [StringLength(512, ErrorMessage = "Instagram URL cannot exceed 512 characters")]
        public string? InstagramUrl { get; set; }

        [StringLength(10, ErrorMessage = "Region code cannot exceed 10 characters")]
        public string? Region { get; set; }

        public List<string>? AllergyKeywords { get; set; }
    }

    /// <summary>
    /// DTO for onboarding profile updates (username + display name)
    /// </summary>
    public class UpdateProfileDto
    {
        [StringLength(30, MinimumLength = 3, ErrorMessage = "Username must be 3-30 characters")]
        [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Only letters, numbers, and underscores allowed")]
        public string? Username { get; set; }

        [StringLength(128, ErrorMessage = "Display name cannot exceed 128 characters")]
        public string? DisplayName { get; set; }
    }
}
