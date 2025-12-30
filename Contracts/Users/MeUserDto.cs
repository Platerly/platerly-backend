namespace Platerly.Api.Contracts.Users;

public sealed class MeUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public bool IsEmailVerified { get; set; }

    public bool IsPremium { get; set; }
    public string? PremiumStatus { get; set; }
    public bool PremiumCancelAtPeriodEnd { get; set; }
    public DateTimeOffset? PremiumCurrentPeriodEnd { get; set; }
}
