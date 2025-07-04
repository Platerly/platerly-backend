namespace Platerly.Api.DTOs
{
    public class UpdateUserDto
    {
        public string? DisplayName { get; set; }
        public string? Bio { get; set; }
        public string? ProfileImageUrl { get; set; }
        public List<string>? AllergyKeywords { get; set; }
    }
}
