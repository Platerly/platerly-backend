namespace Platerly.Api.DTOs
{
    public class FollowUserDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? ProfileImageUrl { get; set; }
    }
}
