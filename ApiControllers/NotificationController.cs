using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Extensions;
using Platerly.Api.Services.Notifications;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2C controller for the authenticated user's in-app notification inbox.
    /// Supports paginated retrieval, unread badge counts, marking as read, and deletion.
    /// Base route: <c>api/notification</c>.
    /// All endpoints require a valid JWT (<c>[Authorize]</c> on the class).
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _notificationService;

        public NotificationController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        /// <summary>
        /// Retrieve a paginated list of notifications for the authenticated user,
        /// ordered by creation date descending (newest first).
        /// </summary>
        /// <param name="page">1-based page number (default 1).</param>
        /// <param name="pageSize">Items per page (default 20).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="NotificationPageDto"/> containing the notifications array,
        /// total count, and unread count.<br/>
        /// <c>401</c> — invalid/missing JWT claim.
        /// </returns>
        [HttpGet]
        public async Task<ActionResult<NotificationPageDto>> GetNotifications(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            if (!User.TryGetUserId(out var userId))
                return Unauthorized("Invalid user token.");

            var result = await _notificationService.GetNotificationsAsync(userId, page, pageSize, ct);
            return Ok(result);
        }

        /// <summary>
        /// Return the current unread notification count for the badge indicator.
        /// Lightweight endpoint safe to poll at high frequency.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ count: int }</c>.<br/>
        /// <c>401</c> — invalid/missing JWT claim.
        /// </returns>
        [HttpGet("unread-count")]
        public async Task<ActionResult<int>> GetUnreadCount(CancellationToken ct = default)
        {
            if (!User.TryGetUserId(out var userId))
                return Unauthorized("Invalid user token.");

            var count = await _notificationService.GetUnreadCountAsync(userId, ct);
            return Ok(new { count });
        }

        /// <summary>
        /// Mark a single notification as read. The notification must belong to the
        /// authenticated user.
        /// </summary>
        /// <param name="id">The notification GUID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> on success.<br/>
        /// <c>401</c> — invalid/missing JWT claim.<br/>
        /// <c>404</c> — notification not found or does not belong to this user.
        /// </returns>
        [HttpPut("{id:guid}/read")]
        public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct = default)
        {
            if (!User.TryGetUserId(out var userId))
                return Unauthorized("Invalid user token.");

            var success = await _notificationService.MarkAsReadAsync(id, userId, ct);

            if (!success)
                return NotFound("Notification not found.");

            return Ok();
        }

        /// <summary>
        /// Mark every unread notification belonging to the authenticated user as read in bulk.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ markedAsRead: int }</c> with the number of records updated.<br/>
        /// <c>401</c> — invalid/missing JWT claim.
        /// </returns>
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead(CancellationToken ct = default)
        {
            if (!User.TryGetUserId(out var userId))
                return Unauthorized("Invalid user token.");

            var count = await _notificationService.MarkAllAsReadAsync(userId, ct);
            return Ok(new { markedAsRead = count });
        }

        /// <summary>
        /// Permanently delete a notification. The notification must belong to the
        /// authenticated user.
        /// </summary>
        /// <param name="id">The notification GUID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — invalid/missing JWT claim.<br/>
        /// <c>404</c> — notification not found or does not belong to this user.
        /// </returns>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteNotification(Guid id, CancellationToken ct = default)
        {
            if (!User.TryGetUserId(out var userId))
                return Unauthorized("Invalid user token.");

            var success = await _notificationService.DeleteNotificationAsync(id, userId, ct);

            if (!success)
                return NotFound("Notification not found.");

            return NoContent();
        }
    }
}
