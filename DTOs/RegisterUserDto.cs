using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.DTOs
{
    public class RegisterUserDto
    {
        [Required(ErrorMessage = "Username is required")]
        [StringLength(64, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 64 characters")]
        [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Username can only contain letters, numbers, and underscores")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters")]
        public string Email { get; set; } = string.Empty;

        [StringLength(128, ErrorMessage = "Display name cannot exceed 128 characters")]
        public string DisplayName { get; set; } = string.Empty;

        [StringLength(1024, ErrorMessage = "Bio cannot exceed 1024 characters")]
        public string Bio { get; set; } = string.Empty;

        [StringLength(1024, ErrorMessage = "Profile image URL cannot exceed 1024 characters")]
        public string? ProfileImageUrl { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 128 characters")]
        public string Password { get; set; } = string.Empty;

        public List<string> AllergyKeywords { get; set; } = new List<string>();
    }
}
