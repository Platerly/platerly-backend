using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.DTOs
{
    public class ResetPasswordDto
    {
        [Required(ErrorMessage = "Token is required")]
        public Guid Token { get; set; }

        [Required(ErrorMessage = "New password is required")]
        [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 128 characters")]
        public string NewPassword { get; set; } = string.Empty;
    }
}
