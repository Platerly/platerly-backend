namespace Platerly.Api.Contracts.Users;

public sealed class PublicUserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Bio { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? BannerImageUrl { get; set; }

    // Optional public stats (only if you already track them)
    public int FollowersCount { get; set; }
    public int FollowingCount { get; set; }
}
