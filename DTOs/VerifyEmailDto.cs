using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.DTOs
{
    public class VerifyEmailDto
    {
        [Required(ErrorMessage = "Token is required")]
        [StringLength(256, ErrorMessage = "Token cannot exceed 256 characters")]
        public string Token { get; set; } = string.Empty;
    }
}
