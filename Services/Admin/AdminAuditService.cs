using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;
using System.Text.Json;

namespace Platerly.Api.Services.Admin
{
    /// <summary>
    /// Infrastructure layer — writes immutable audit trail records for all admin actions.
    /// Depends on <see cref="PlaterlyDbContext"/> and <see cref="ILogger{AdminAuditService}"/>.
    ///
    /// Every admin operation in the system (user bans, ingredient edits, content moderation, etc.)
    /// should call <see cref="LogAsync"/> on success or <see cref="LogFailureAsync"/> on failure.
    /// Audit logs are never deleted and provide a regulatory-grade activity trail.
    ///
    /// <c>OldValues</c> and <c>NewValues</c> are serialised as camelCase JSON for diff-readability.
    /// <c>UserAgent</c> is capped at 512 characters; <c>ErrorMessage</c> at 1,024 characters.
    ///
    /// TODO (retention): The AdminAuditLogs table grows unbounded. Before production traffic
    /// reaches significant scale, implement a background archival job that moves rows older than
    /// 90 days to a cold-storage table (e.g. AdminAuditLogsArchive) or exports them to Azure Blob
    /// Storage as NDJSON and then deletes the originals. A hosted service (IHostedService) running
    /// nightly via a cron-style timer is the recommended approach. PIPEDA/GDPR may also require
    /// that PII in OldValues/NewValues snapshots is redacted after the retention window.
    /// </summary>
    public class AdminAuditService : IAdminAuditService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<AdminAuditService> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public AdminAuditService(
            PlaterlyDbContext db,
            ILogger<AdminAuditService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Records a successful admin action. Writes an <see cref="AdminAuditLog"/> row with
        /// <c>WasSuccessful=true</c> and mirrors key fields to the structured log for Sentry visibility.
        /// </summary>
        /// <param name="adminUserId">ID of the admin user performing the action.</param>
        /// <param name="action">The type of admin action taken.</param>
        /// <param name="entityType">The entity type being acted on (e.g. "User", "Recipe").</param>
        /// <param name="entityId">The ID of the affected entity, or <c>null</c> if not applicable.</param>
        /// <param name="description">Human-readable description of what was done.</param>
        /// <param name="oldValues">Optional: the state of the entity before the action (serialised to JSON).</param>
        /// <param name="newValues">Optional: the state of the entity after the action (serialised to JSON).</param>
        /// <param name="ipAddress">Optional: the IP address of the requesting admin.</param>
        /// <param name="userAgent">Optional: the User-Agent string (truncated to 512 chars).</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task LogAsync(
            Guid adminUserId,
            AdminAction action,
            string entityType,
            string? entityId,
            string description,
            object? oldValues = null,
            object? newValues = null,
            string? ipAddress = null,
            string? userAgent = null,
            CancellationToken ct = default)
        {
            var log = new AdminAuditLog
            {
                AdminUserId = adminUserId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Description = description,
                OldValues = oldValues != null ? JsonSerializer.Serialize(oldValues, _jsonOptions) : null,
                NewValues = newValues != null ? JsonSerializer.Serialize(newValues, _jsonOptions) : null,
                IpAddress = ipAddress,
                UserAgent = userAgent?.Length > 512 ? userAgent[..512] : userAgent,
                CreatedAt = DateTime.UtcNow,
                WasSuccessful = true
            };

            _db.AdminAuditLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Admin audit: {Action} on {EntityType} ({EntityId}) by user {AdminUserId}",
                action, entityType, entityId, adminUserId);
        }

        /// <summary>
        /// Records a failed admin action. Writes an <see cref="AdminAuditLog"/> row with
        /// <c>WasSuccessful=false</c> and the error message (truncated to 1,024 chars).
        /// Emits a <c>LogWarning</c> entry for Sentry alerting.
        /// </summary>
        /// <param name="adminUserId">ID of the admin user who attempted the action.</param>
        /// <param name="action">The type of admin action that failed.</param>
        /// <param name="entityType">The entity type that was the target.</param>
        /// <param name="entityId">The ID of the target entity, or <c>null</c>.</param>
        /// <param name="description">Human-readable description of the attempted action.</param>
        /// <param name="errorMessage">The error or reason for failure (capped at 1,024 characters).</param>
        /// <param name="ipAddress">Optional: requesting IP address.</param>
        /// <param name="userAgent">Optional: User-Agent string (truncated to 512 chars).</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task LogFailureAsync(
            Guid adminUserId,
            AdminAction action,
            string entityType,
            string? entityId,
            string description,
            string errorMessage,
            string? ipAddress = null,
            string? userAgent = null,
            CancellationToken ct = default)
        {
            var log = new AdminAuditLog
            {
                AdminUserId = adminUserId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Description = description,
                IpAddress = ipAddress,
                UserAgent = userAgent?.Length > 512 ? userAgent[..512] : userAgent,
                CreatedAt = DateTime.UtcNow,
                WasSuccessful = false,
                ErrorMessage = errorMessage.Length > 1024 ? errorMessage[..1024] : errorMessage
            };

            _db.AdminAuditLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            _logger.LogWarning(
                "Admin audit FAILED: {Action} on {EntityType} ({EntityId}) by user {AdminUserId}: {Error}",
                action, entityType, entityId, adminUserId, errorMessage);
        }

        /// <summary>
        /// Returns a paginated list of audit log entries. All filter parameters are optional and
        /// composable; null values are ignored. Results are ordered newest-first.
        /// </summary>
        /// <param name="adminUserId">Filter to actions by a specific admin user.</param>
        /// <param name="action">Filter to a specific <see cref="AdminAction"/> type.</param>
        /// <param name="entityType">Filter to a specific entity type string.</param>
        /// <param name="fromDate">Inclusive lower bound on <c>CreatedAt</c>.</param>
        /// <param name="toDate">Inclusive upper bound on <c>CreatedAt</c>.</param>
        /// <param name="page">1-based page number.</param>
        /// <param name="pageSize">Number of records per page (default 50).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Matching <see cref="AdminAuditLog"/> records with the <c>AdminUser</c> navigation included.</returns>
        public async Task<List<AdminAuditLog>> GetLogsAsync(
            Guid? adminUserId = null,
            AdminAction? action = null,
            string? entityType = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default)
        {
            var query = _db.AdminAuditLogs
                .Include(l => l.AdminUser)
                .AsQueryable();

            if (adminUserId.HasValue)
                query = query.Where(l => l.AdminUserId == adminUserId.Value);

            if (action.HasValue)
                query = query.Where(l => l.Action == action.Value);

            if (!string.IsNullOrEmpty(entityType))
                query = query.Where(l => l.EntityType == entityType);

            if (fromDate.HasValue)
                query = query.Where(l => l.CreatedAt >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(l => l.CreatedAt <= toDate.Value);

            return await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .AsNoTracking()
                .ToListAsync(ct);
        }

        /// <summary>
        /// Returns the total count of audit log entries matching the given filters.
        /// Intended for pagination — call alongside <see cref="GetLogsAsync"/> with the same parameters.
        /// </summary>
        /// <param name="adminUserId">Optional admin user filter.</param>
        /// <param name="action">Optional action type filter.</param>
        /// <param name="entityType">Optional entity type filter.</param>
        /// <param name="fromDate">Optional inclusive start date.</param>
        /// <param name="toDate">Optional inclusive end date.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Count of matching log entries.</returns>
        public async Task<int> GetLogCountAsync(
            Guid? adminUserId = null,
            AdminAction? action = null,
            string? entityType = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            CancellationToken ct = default)
        {
            var query = _db.AdminAuditLogs.AsQueryable();

            if (adminUserId.HasValue)
                query = query.Where(l => l.AdminUserId == adminUserId.Value);

            if (action.HasValue)
                query = query.Where(l => l.Action == action.Value);

            if (!string.IsNullOrEmpty(entityType))
                query = query.Where(l => l.EntityType == entityType);

            if (fromDate.HasValue)
                query = query.Where(l => l.CreatedAt >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(l => l.CreatedAt <= toDate.Value);

            return await query.CountAsync(ct);
        }
    }
}
