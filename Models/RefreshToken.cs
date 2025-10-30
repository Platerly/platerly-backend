using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System;

namespace Platerly.Api.Models
{
    public class RefreshToken
    {
        [Key] public int Id { get; set; }

        [Required] public Guid UserId { get; set; }
        [ForeignKey(nameof(UserId))] public User User { get; set; } = default!;

        // Store only a hash of the refresh token
        [Required, MaxLength(128)]
        public string TokenHash { get; set; } = default!;

        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public string? ReplacedByTokenHash { get; set; }

        // Optional metadata
        [MaxLength(256)] public string? UserAgent { get; set; }
        [MaxLength(64)] public string? IpAddress { get; set; }

        [NotMapped]
        public bool IsActive => RevokedAtUtc == null && DateTime.UtcNow < ExpiresAtUtc;
    }
}
