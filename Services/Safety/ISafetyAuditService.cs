using Platerly.Api.Models;

namespace Platerly.Api.Services.Safety
{
    /// <summary>Writes and queries safety audit log entries for safety-endpoint calls; implementations must swallow exceptions.</summary>
    public interface ISafetyAuditService
    {
        /// <summary>
        /// Write an audit record for a completed safety-endpoint call.
        /// Fire-and-forget safe — implementations must not throw.
        /// </summary>
        Task LogAsync(SafetyAuditLog entry, CancellationToken ct = default);

        /// <summary>
        /// Query audit logs with optional filters. Returns most-recent first.
        /// </summary>
        Task<List<SafetyAuditLog>> GetLogsAsync(
            DateTime? from = null,
            DateTime? to = null,
            Guid? userId = null,
            string? severity = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default);
    }
}
