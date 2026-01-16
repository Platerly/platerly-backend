using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Extensions;
using Platerly.Api.Services.Notifications;

namespace Platerly.Api.ApiControllers
{
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
        /// Get paginated notifications for the current user
        /// </summary>
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
        /// Get unread notification count
        /// </summary>
        [HttpGet("unread-count")]
        public async Task<ActionResult<int>> GetUnreadCount(CancellationToken ct = default)
        {
            if (!User.TryGetUserId(out var userId))
                return Unauthorized("Invalid user token.");

            var count = await _notificationService.GetUnreadCountAsync(userId, ct);
            return Ok(new { count });
        }

        /// <summary>
        /// Mark a single notification as read
        /// </summary>
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
        /// Mark all notifications as read
        /// </summary>
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead(CancellationToken ct = default)
        {
            if (!User.TryGetUserId(out var userId))
                return Unauthorized("Invalid user token.");

            var count = await _notificationService.MarkAllAsReadAsync(userId, ct);
            return Ok(new { markedAsRead = count });
        }

        /// <summary>
        /// Delete a notification
        /// </summary>
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
