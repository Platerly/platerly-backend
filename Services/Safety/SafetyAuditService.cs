using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Safety
{
    public class SafetyAuditService : ISafetyAuditService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<SafetyAuditService> _logger;

        public SafetyAuditService(PlaterlyDbContext db, ILogger<SafetyAuditService> logger)
        {
            _db = db;
            _logger = logger;
        }

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
