namespace Platerly.Api.DTOs
{
    public class DeleteAccountDto
    {
        public string? Confirmation { get; set; }
    }

    public class UserDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Bio { get; set; }
        public string? ProfileImageUrl { get; set; }
        public bool IsEmailVerified { get; set; }
        public DateTime CreatedAt { get; set; }

        // profile links (used by settings/edit form)
        public string? WebsiteUrl { get; set; }
        public string? YouTubeUrl { get; set; }
        public string? XUrl { get; set; }
        public string? InstagramUrl { get; set; }

        // handy if you also want to show/edit banner in settings
        public string? BannerImageUrl { get; set; }

        // ===== Premium / billing info =====
        public bool IsPremium { get; set; }
        public string? PremiumStatus { get; set; }                  // active, trialing, canceled, etc.
        public DateTimeOffset? PremiumSince { get; set; }
        public bool? PremiumCancelAtPeriodEnd { get; set; }
        public DateTimeOffset? PremiumCurrentPeriodEnd { get; set; }
        public string? BillingEmail { get; set; }

        // ===== Role info =====
        public string? Role { get; set; }  // User, Moderator, Admin, SuperAdmin
    }
}
