using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;
using System.Text.Json;

namespace Platerly.Api.Services.Admin
{
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
