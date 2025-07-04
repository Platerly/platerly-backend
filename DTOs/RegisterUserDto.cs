namespace Platerly.Api.DTOs
{
    public class RegisterUserDto
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public string? ProfileImageUrl { get; set; }
        public string Password { get; set; } = string.Empty;
        public List<string> AllergyKeywords { get; set; } = new List<string>();

    }
}
