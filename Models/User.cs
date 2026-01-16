using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.Models
{
    /// <summary>
    /// User roles for authorization
    /// </summary>
    public enum UserRole
    {
        User = 0,           // Regular user
        Moderator = 1,      // Can moderate content
        Admin = 2,          // Full admin access
        SuperAdmin = 3      // Can manage other admins
    }

    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid(); // Unique user ID

        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        // Role-based access control
        public UserRole Role { get; set; } = UserRole.User;
        public DateTime? RoleAssignedAt { get; set; }
        public Guid? RoleAssignedBy { get; set; } // Which admin assigned this role

        public string PasswordHash { get; set; } = string.Empty;

        public string? DisplayName { get; set; }
        public string? Bio { get; set; }

        public string? ProfileImageUrl { get; set; }
        public string? ProfileImageBlobName { get; set; } // Blob storage name for the profile image

        [MaxLength(512)]
        public string? BannerImageUrl { get; set; } // SAS URL for the banner image

        [MaxLength(256)]
        public string? BannerImageBlobName { get; set; } // Blob storage name for the banner image

        public DateTime CreatedAt { get; set; }
        public bool IsEmailVerified { get; set; } = false;

        // Optimistic concurrency
        public byte[]? RowVersion { get; set; } // Used for concurrency checks

        [MaxLength(200)]
        public string? WebsiteUrl { get; set; }

        [MaxLength(200)]
        public string? YoutubeUrl { get; set; }
        
        [MaxLength(200)]
        public string? XUrl { get; set; } // Formerly Twitter

        [MaxLength(200)]
        public string? InstagramUrl { get; set; }

        // Region preference for trending/personalization
        [MaxLength(64)]
        public string? Region { get; set; }

        [MaxLength(2)]
        public string? CountryCode { get; set; }

        // Navigation properties (relationships)
        public ICollection<Recipe>? Recipes { get; set; }
        public ICollection<Cookbook>? Cookbooks { get; set; }

        // Follow / Following  (relationships)
        public ICollection<UserFollow>? Followers { get; set; } // People following this user
        public ICollection<UserFollow>? Following { get; set; } // People this user follows

        // user allergy
        public List<string> AllergyKeywords { get; set; } = new List<string>();
        public bool AllergyAlertsEnabled { get; set; } = true;

        public ICollection<UserAllergy> Allergies { get; set; } = new List<UserAllergy>();

        public ICollection<PasswordResetToken> PasswordResetTokens { get; set; }

        //stripe
        public bool IsPremium { get; set; } = false;
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }
        public string? PremiumStatus { get; set; } // active, past_due, canceled, unpaid
        public DateTimeOffset? PremiumSince { get; set; }
        public bool? PremiumCancelAtPeriodEnd { get; set; }           // mirrors Stripe cancel_at_period_end
        public DateTimeOffset? PremiumCurrentPeriodEnd { get; set; }  // mirrors subscription.current_period_end
        public string? BillingEmail { get; set; }                     // keep a billing contact if you want

        public DateTimeOffset? TrialStartUtc { get; set; }
        public DateTimeOffset? TrialEndUtc { get; set; }

        // External OAuth providers
        [MaxLength(128)]
        public string? GoogleId { get; set; }

        [MaxLength(128)]
        public string? FacebookId { get; set; }

        [MaxLength(128)]
        public string? AppleId { get; set; }

        /// <summary>
        /// How the user originally signed up: "local", "google", "facebook", "apple"
        /// </summary>
        [MaxLength(20)]
        public string AuthProvider { get; set; } = "local";

        // Pending email change (for email change verification flow)
        [MaxLength(256)]
        public string? PendingEmail { get; set; }

        [MaxLength(128)]
        public string? PendingEmailToken { get; set; }

        public DateTime? PendingEmailTokenExpiry { get; set; }
    }
}
