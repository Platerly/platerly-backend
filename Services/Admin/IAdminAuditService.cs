using Platerly.Api.Models;

namespace Platerly.Api.Services.Admin
{
    public interface IAdminAuditService
    {
        /// <summary>
        /// Log an admin action
        /// </summary>
        Task LogAsync(
            Guid adminUserId,
            AdminAction action,
            string entityType,
            string? entityId,
            string description,
            object? oldValues = null,
            object? newValues = null,
            string? ipAddress = null,
            string? userAgent = null,
            CancellationToken ct = default);

        /// <summary>
        /// Log a failed admin action
        /// </summary>
        Task LogFailureAsync(
            Guid adminUserId,
            AdminAction action,
            string entityType,
            string? entityId,
            string description,
            string errorMessage,
            string? ipAddress = null,
            string? userAgent = null,
            CancellationToken ct = default);

        /// <summary>
        /// Get audit logs with filtering
        /// </summary>
        Task<List<AdminAuditLog>> GetLogsAsync(
            Guid? adminUserId = null,
            AdminAction? action = null,
            string? entityType = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default);

        /// <summary>
        /// Get audit log count
        /// </summary>
        Task<int> GetLogCountAsync(
            Guid? adminUserId = null,
            AdminAction? action = null,
            string? entityType = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            CancellationToken ct = default);
    }
}
