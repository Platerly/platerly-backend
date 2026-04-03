using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Safety
{
    /// <summary>
    /// Infrastructure layer — persists immutable safety-layer audit records and exposes
    /// a filtered query interface for the admin dashboard.
    /// Depends on <see cref="PlaterlyDbContext"/> and <see cref="ILogger{SafetyAuditService}"/>.
    ///
    /// Every call to a safety-layer endpoint (allergen scan, nickel analysis, recipe safety audit)
    /// should invoke <see cref="LogAsync"/> so that request-level metadata — HTTP method, endpoint,
    /// user/API key, IP address, response status, alert count, severity, confidence — is durably stored.
    ///
    /// Errors during audit writes are swallowed: a logging failure must never propagate to the caller.
    /// The entry is also mirrored to the structured log for Sentry and log-stream visibility.
    /// </summary>
    public class SafetyAuditService : ISafetyAuditService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<SafetyAuditService> _logger;

        public SafetyAuditService(PlaterlyDbContext db, ILogger<SafetyAuditService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Writes a <see cref="SafetyAuditLog"/> entry to the database and mirrors it to the
        /// structured log. Exceptions are caught and logged at <c>Error</c> level; they do not
        /// propagate to the caller.
        /// </summary>
        /// <param name="entry">Fully populated audit log entry. Caller is responsible for setting all fields.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task LogAsync(SafetyAuditLog entry, CancellationToken ct = default)
        {
            try
            {
                _db.SafetyAuditLogs.Add(entry);
                await _db.SaveChangesAsync(ct);

                // Mirror to structured log for Sentry/log stream visibility
                _logger.LogInformation(
                    "SafetyAudit | {Method} {Endpoint} | user={UserId} key={ApiKeyId} ip={Ip} " +
                    "status={Status} ms={Ms} alerts={Alerts} severity={Severity} confidence={Confidence}",
                    entry.HttpMethod, entry.Endpoint, entry.UserId, entry.ApiKeyId,
                    entry.IpAddress, entry.ResponseStatusCode, entry.ResponseTimeMs,
                    entry.AlertCount, entry.HighestAlertSeverity ?? "none", entry.ConfidenceLevel ?? "n/a");
            }
            catch (Exception ex)
            {
                // Never let audit failure propagate to the caller
                _logger.LogError(ex, "SafetyAudit: failed to write audit log for {Endpoint}", entry.Endpoint);
            }
        }

        /// <summary>
        /// Returns a paginated, newest-first list of safety audit log entries.
        /// All filter parameters are optional and composable.
        /// </summary>
        /// <param name="from">Optional inclusive lower bound on <c>TimestampUtc</c>.</param>
        /// <param name="to">Optional inclusive upper bound on <c>TimestampUtc</c>.</param>
        /// <param name="userId">Optional filter to a specific user.</param>
        /// <param name="severity">Optional severity string (case-insensitive, compared uppercase in DB).</param>
        /// <param name="page">1-based page number.</param>
        /// <param name="pageSize">Records per page (default 50).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Matching <see cref="SafetyAuditLog"/> rows ordered by <c>TimestampUtc</c> descending.</returns>
        public async Task<List<SafetyAuditLog>> GetLogsAsync(
            DateTime? from = null,
            DateTime? to = null,
            Guid? userId = null,
            string? severity = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            var query = _db.SafetyAuditLogs.AsNoTracking().AsQueryable();

            if (from.HasValue)
                query = query.Where(l => l.TimestampUtc >= from.Value);
            if (to.HasValue)
                query = query.Where(l => l.TimestampUtc <= to.Value);
            if (userId.HasValue)
                query = query.Where(l => l.UserId == userId.Value);
            if (!string.IsNullOrEmpty(severity))
                query = query.Where(l => l.HighestAlertSeverity == severity.ToUpper());

            return await query
                .OrderByDescending(l => l.TimestampUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);
        }
    }
}
