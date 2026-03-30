using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Domains permanently blocked from indexing — either at the blogger's request
    /// or added manually by an admin. The indexer checks this table before touching
    /// any domain, regardless of robots.txt status.
    /// </summary>
    [Index(nameof(Domain), IsUnique = true)]
    public class BlockedDomain
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(256)]
        public string Domain { get; set; } = string.Empty;

        public DateTime BlockedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(1024)]
        public string? Reason { get; set; }

        /// <summary>
        /// Contact email of the blogger or person who requested removal, if applicable.
        /// </summary>
        [MaxLength(256)]
        public string? ContactEmail { get; set; }
    }
}
