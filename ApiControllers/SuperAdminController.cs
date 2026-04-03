using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;
using Platerly.Api.Services;
using Platerly.Api.Services.Admin;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// Restricted admin controller for managing the admin/moderator user roster and
    /// viewing platform security statistics. Accessible only to users with the
    /// <c>SuperAdmin</c> role.
    /// Base route: <c>api/superadmin</c>.
    /// All role changes are persisted to <c>AdminAuditLogs</c> and trigger email
    /// notifications to the affected user via <see cref="IEmailSender"/>.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "SuperAdmin")]
    public class SuperAdminController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly IAdminAuditService _audit;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<SuperAdminController> _logger;

        public SuperAdminController(
            PlaterlyDbContext db,
            IAdminAuditService audit,
            IEmailSender emailSender,
            ILogger<SuperAdminController> logger)
        {
            _db = db;
            _audit = audit;
            _emailSender = emailSender;
            _logger = logger;
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
        }

        private string? GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString();
        private string? GetUserAgent() => HttpContext.Request.Headers["User-Agent"].ToString();

        // ========== Admin User Management ==========

        /// <summary>
        /// Retrieve all users with elevated roles (Moderator, Admin, SuperAdmin),
        /// ordered by role then username.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — array of <see cref="AdminUserDto"/>.</returns>
        [HttpGet("admins")]
        public async Task<ActionResult<List<AdminUserDto>>> GetAdminUsers(CancellationToken ct = default)
        {
            var admins = await _db.Users
                .Where(u => u.Role != UserRole.User)
                .OrderBy(u => u.Role)
                .ThenBy(u => u.Username)
                .Select(u => new AdminUserDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    DisplayName = u.DisplayName,
                    Role = u.Role.ToString(),
                    RoleAssignedAt = u.RoleAssignedAt,
                    RoleAssignedBy = u.RoleAssignedBy,
                    CreatedAt = u.CreatedAt,
                    IsEmailVerified = u.IsEmailVerified
                })
                .AsNoTracking()
                .ToListAsync(ct);

            return Ok(admins);
        }

        /// <summary>
        /// Promote a user to a higher role (Moderator, Admin, or SuperAdmin).
        /// Only SuperAdmins can grant the SuperAdmin role. Attempting to "promote" a user
        /// who already holds an equal or higher role returns 400 — use <c>/demote</c> instead.
        /// The action is audit logged and the affected user receives a promotion email.
        /// </summary>
        /// <param name="dto"><c>UserId</c> of the target user and <c>NewRole</c> string.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ message, userId, oldRole, newRole }</c>.<br/>
        /// <c>400</c> — invalid role or user already at or above the requested role.<br/>
        /// <c>403</c> — insufficient privileges to grant the requested role.<br/>
        /// <c>404</c> — user not found.
        /// </returns>
        [HttpPost("promote")]
        public async Task<IActionResult> PromoteUser(
            [FromBody] PromoteUserDto dto,
            CancellationToken ct = default)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Unauthorized();

            var user = await _db.Users.FindAsync(new object[] { dto.UserId }, ct);
            if (user == null)
                return NotFound("User not found");

            if (!Enum.TryParse<UserRole>(dto.NewRole, true, out var newRole))
                return BadRequest("Invalid role. Valid roles: Moderator, Admin, SuperAdmin");

            // Cannot promote to SuperAdmin unless you're also a SuperAdmin (already enforced by controller attribute)
            // But let's add extra safety: only existing SuperAdmins can create new SuperAdmins
            if (newRole == UserRole.SuperAdmin)
            {
                var currentUser = await _db.Users.FindAsync(new object[] { currentUserId.Value }, ct);
                if (currentUser?.Role != UserRole.SuperAdmin)
                    return Forbid("Only SuperAdmins can create other SuperAdmins");
            }

            var oldRole = user.Role;

            // Prevent downgrade through this endpoint
            if ((int)newRole <= (int)oldRole && oldRole != UserRole.User)
            {
                return BadRequest($"User already has role {oldRole}. Use /demote endpoint to lower their role.");
            }

            user.Role = newRole;
            user.RoleAssignedAt = DateTime.UtcNow;
            user.RoleAssignedBy = currentUserId;

            await _db.SaveChangesAsync(ct);

            // Audit log
            await _audit.LogAsync(
                currentUserId.Value,
                AdminAction.PromoteUser,
                "User",
                user.Id.ToString(),
                $"Promoted user '{user.Username}' from {oldRole} to {newRole}",
                new { OldRole = oldRole.ToString() },
                new { NewRole = newRole.ToString() },
                GetIpAddress(),
                GetUserAgent(),
                ct);

            _logger.LogWarning(
                "SECURITY: User {UserId} ({Username}) promoted from {OldRole} to {NewRole} by {AdminId}",
                user.Id, user.Username, oldRole, newRole, currentUserId);

            // Send email notification to the promoted user
            try
            {
                await SendPromotionEmailAsync(user, oldRole, newRole);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send promotion email to {Email}", user.Email);
                // Don't fail the promotion if email fails
            }

            return Ok(new
            {
                message = $"User '{user.Username}' promoted to {newRole}",
                userId = user.Id,
                oldRole = oldRole.ToString(),
                newRole = newRole.ToString()
            });
        }

        /// <summary>
        /// Demote an admin or moderator to a lower role (defaults to regular User).
        /// A SuperAdmin cannot demote themselves. Only SuperAdmins can demote other SuperAdmins.
        /// The new role must be strictly lower than the current role.
        /// The action is audit logged and the affected user receives a demotion email.
        /// </summary>
        /// <param name="dto"><c>UserId</c> of the target user and optional <c>NewRole</c>
        /// (defaults to "User" if omitted or unparseable).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ message, userId, oldRole, newRole }</c>.<br/>
        /// <c>400</c> — self-demotion or new role not lower than current role.<br/>
        /// <c>403</c> — insufficient privileges.<br/>
        /// <c>404</c> — user not found.
        /// </returns>
        [HttpPost("demote")]
        public async Task<IActionResult> DemoteUser(
            [FromBody] DemoteUserDto dto,
            CancellationToken ct = default)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Unauthorized();

            var user = await _db.Users.FindAsync(new object[] { dto.UserId }, ct);
            if (user == null)
                return NotFound("User not found");

            // Cannot demote yourself
            if (user.Id == currentUserId.Value)
                return BadRequest("You cannot demote yourself");

            // Only SuperAdmin can demote other SuperAdmins
            if (user.Role == UserRole.SuperAdmin)
            {
                var currentUser = await _db.Users.FindAsync(new object[] { currentUserId.Value }, ct);
                if (currentUser?.Role != UserRole.SuperAdmin)
                    return Forbid("Only SuperAdmins can demote other SuperAdmins");
            }

            var oldRole = user.Role;

            if (!Enum.TryParse<UserRole>(dto.NewRole, true, out var newRole))
            {
                newRole = UserRole.User; // Default to regular user
            }

            // Ensure we're actually demoting
            if ((int)newRole >= (int)oldRole)
            {
                return BadRequest($"New role must be lower than current role ({oldRole})");
            }

            user.Role = newRole;
            user.RoleAssignedAt = DateTime.UtcNow;
            user.RoleAssignedBy = currentUserId;

            await _db.SaveChangesAsync(ct);

            // Audit log
            await _audit.LogAsync(
                currentUserId.Value,
                AdminAction.DemoteUser,
                "User",
                user.Id.ToString(),
                $"Demoted user '{user.Username}' from {oldRole} to {newRole}",
                new { OldRole = oldRole.ToString() },
                new { NewRole = newRole.ToString() },
                GetIpAddress(),
                GetUserAgent(),
                ct);

            _logger.LogWarning(
                "SECURITY: User {UserId} ({Username}) demoted from {OldRole} to {NewRole} by {AdminId}",
                user.Id, user.Username, oldRole, newRole, currentUserId);

            // Send email notification to the demoted user
            try
            {
                await SendDemotionEmailAsync(user, oldRole, newRole);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send demotion email to {Email}", user.Email);
                // Don't fail the demotion if email fails
            }

            return Ok(new
            {
                message = $"User '{user.Username}' demoted to {newRole}",
                userId = user.Id,
                oldRole = oldRole.ToString(),
                newRole = newRole.ToString()
            });
        }

        /// <summary>
        /// Search for any user by username, email, or display name. Used to look up candidates
        /// before promoting them. Requires at least 2 characters.
        /// </summary>
        /// <param name="query">Search term (minimum 2 characters).</param>
        /// <param name="take">Maximum results, clamped 1–50 (default 20).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — array of <see cref="UserSearchResultDto"/>
        /// (id, username, email, displayName, role, createdAt, isEmailVerified).<br/>
        /// <c>400</c> — query too short.
        /// </returns>
        [HttpGet("users/search")]
        public async Task<ActionResult<List<UserSearchResultDto>>> SearchUsers(
            [FromQuery] string query,
            [FromQuery] int take = 20,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return BadRequest("Search query must be at least 2 characters");

            take = Math.Clamp(take, 1, 50);
            var queryLower = query.ToLower();

            var users = await _db.Users
                .Where(u =>
                    u.Username.ToLower().Contains(queryLower) ||
                    u.Email.ToLower().Contains(queryLower) ||
                    (u.DisplayName != null && u.DisplayName.ToLower().Contains(queryLower)))
                .OrderBy(u => u.Username)
                .Take(take)
                .Select(u => new UserSearchResultDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    DisplayName = u.DisplayName,
                    Role = u.Role.ToString(),
                    CreatedAt = u.CreatedAt,
                    IsEmailVerified = u.IsEmailVerified
                })
                .AsNoTracking()
                .ToListAsync(ct);

            return Ok(users);
        }

        /// <summary>
        /// Return a real-time security dashboard snapshot including admin user counts by role,
        /// audit log activity volumes for the past 24 hours and 7 days, failed admin action counts,
        /// and role change (promote/demote) counts for the past week.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — anonymous object with <c>adminUsers</c>, <c>auditActivity</c>,
        /// <c>roleChanges</c>, and <c>generatedAt</c> (UTC timestamp).
        /// </returns>
        [HttpGet("security-stats")]
        public async Task<ActionResult<object>> GetSecurityStats(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var last24h = now.AddHours(-24);
            var last7d = now.AddDays(-7);

            var adminCount = await _db.Users.CountAsync(u => u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin, ct);
            var moderatorCount = await _db.Users.CountAsync(u => u.Role == UserRole.Moderator, ct);
            var superAdminCount = await _db.Users.CountAsync(u => u.Role == UserRole.SuperAdmin, ct);

            var auditLogs24h = await _db.AdminAuditLogs.CountAsync(l => l.CreatedAt >= last24h, ct);
            var auditLogs7d = await _db.AdminAuditLogs.CountAsync(l => l.CreatedAt >= last7d, ct);
            var failedActions24h = await _db.AdminAuditLogs.CountAsync(l => l.CreatedAt >= last24h && !l.WasSuccessful, ct);

            var recentPromotions = await _db.AdminAuditLogs
                .Where(l => l.Action == AdminAction.PromoteUser && l.CreatedAt >= last7d)
                .CountAsync(ct);

            var recentDemotions = await _db.AdminAuditLogs
                .Where(l => l.Action == AdminAction.DemoteUser && l.CreatedAt >= last7d)
                .CountAsync(ct);

            return Ok(new
            {
                adminUsers = new
                {
                    superAdmins = superAdminCount,
                    admins = adminCount,
                    moderators = moderatorCount,
                    total = superAdminCount + adminCount + moderatorCount
                },
                auditActivity = new
                {
                    last24Hours = auditLogs24h,
                    last7Days = auditLogs7d,
                    failedActionsLast24h = failedActions24h
                },
                roleChanges = new
                {
                    promotionsLast7Days = recentPromotions,
                    demotionsLast7Days = recentDemotions
                },
                generatedAt = now
            });
        }

        // ========== Email Helpers ==========

        private async Task SendPromotionEmailAsync(User user, UserRole oldRole, UserRole newRole)
        {
            var subject = $"[Recipzee] You've been promoted to {newRole}";

            var roleDescription = newRole switch
            {
                UserRole.Moderator => "As a Moderator, you can now review and moderate recipe content, manage comments, and help maintain community standards.",
                UserRole.Admin => "As an Admin, you have access to the admin dashboard where you can manage prohibited ingredients, view analytics, and perform administrative tasks.",
                UserRole.SuperAdmin => "As a SuperAdmin, you have full administrative access including the ability to manage other admin users, view audit logs, and configure system settings.",
                _ => "Your account has been updated with new permissions."
            };

            var body = $@"
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
        <h2 style='color: #2c5530;'>Congratulations, {user.DisplayName ?? user.Username}!</h2>

        <p>Your role on Recipzee has been upgraded from <strong>{oldRole}</strong> to <strong>{newRole}</strong>.</p>

        <div style='background-color: #f0f7f1; border-left: 4px solid #2c5530; padding: 15px; margin: 20px 0;'>
            <p style='margin: 0;'>{roleDescription}</p>
        </div>

        <p>With this new role comes additional responsibilities. Please ensure you:</p>
        <ul>
            <li>Follow all platform guidelines and policies</li>
            <li>Act with integrity and fairness</li>
            <li>Report any security concerns immediately</li>
            <li>Keep your account credentials secure</li>
        </ul>

        <p>If you have any questions about your new responsibilities, please reach out to the admin team.</p>

        <hr style='border: none; border-top: 1px solid #ddd; margin: 30px 0;'>
        <p style='font-size: 12px; color: #666;'>
            This is an automated message from Recipzee. If you did not expect this change, please contact support immediately.
        </p>
    </div>
</body>
</html>";

            await _emailSender.SendEmailAsync(user.Email, subject, body);
        }

        private async Task SendDemotionEmailAsync(User user, UserRole oldRole, UserRole newRole)
        {
            var subject = $"[Recipzee] Your role has been updated";

            var newRoleDescription = newRole switch
            {
                UserRole.User => "You now have standard user access to Recipzee.",
                UserRole.Moderator => "You retain Moderator access and can continue to help moderate content.",
                UserRole.Admin => "You retain Admin access to the admin dashboard.",
                _ => "Your account permissions have been updated."
            };

            var body = $@"
<html>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
        <h2 style='color: #2c5530;'>Hello, {user.DisplayName ?? user.Username}</h2>

        <p>Your role on Recipzee has been changed from <strong>{oldRole}</strong> to <strong>{newRole}</strong>.</p>

        <div style='background-color: #fff3cd; border-left: 4px solid #856404; padding: 15px; margin: 20px 0;'>
            <p style='margin: 0;'>{newRoleDescription}</p>
        </div>

        <p>If you believe this change was made in error or have questions, please contact the admin team.</p>

        <hr style='border: none; border-top: 1px solid #ddd; margin: 30px 0;'>
        <p style='font-size: 12px; color: #666;'>
            This is an automated message from Recipzee. Your access to previous admin features may have been revoked.
        </p>
    </div>
</body>
</html>";

            await _emailSender.SendEmailAsync(user.Email, subject, body);
        }
    }

    // DTOs for SuperAdmin endpoints
    public class AdminUserDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string Role { get; set; } = string.Empty;
        public DateTime? RoleAssignedAt { get; set; }
        public Guid? RoleAssignedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsEmailVerified { get; set; }
    }

    public class PromoteUserDto
    {
        public Guid UserId { get; set; }
        public string NewRole { get; set; } = "Admin"; // Moderator, Admin, SuperAdmin
    }

    public class DemoteUserDto
    {
        public Guid UserId { get; set; }
        public string NewRole { get; set; } = "User"; // User, Moderator, Admin
    }

    public class UserSearchResultDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string Role { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public bool IsEmailVerified { get; set; }
    }
}
