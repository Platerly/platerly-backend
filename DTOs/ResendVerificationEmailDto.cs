using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.DTOs
{
    public class ResendVerificationEmailDto
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters")]
        public string Email { get; set; } = string.Empty;
    }
}
