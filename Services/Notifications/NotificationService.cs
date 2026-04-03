using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Notifications
{
    /// <summary>
    /// B2C infrastructure service — creates and retrieves in-app notifications.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Key behaviours:
    /// <list type="bullet">
    ///   <item>Self-notification suppression: <see cref="CreateNotificationAsync"/> returns early
    ///     when <c>userId == actorId</c>, preventing actors from notifying themselves.</item>
    ///   <item>Trending deduplication: <see cref="NotifyRecipeTrendingAsync"/> checks for an
    ///     existing trending notification within the last 24 hours and skips creation if found,
    ///     preventing notification spam for persistently trending recipes.</item>
    ///   <item>All <c>Notify*</c> convenience methods delegate to
    ///     <see cref="CreateNotificationAsync"/> with the appropriate <see cref="NotificationType"/>.</item>
    ///   <item>Comment preview is truncated to 100 characters with "..." appended if longer.</item>
    ///   <item>Page size is clamped 1–50; page is clamped to a minimum of 1.</item>
    /// </list>
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(PlaterlyDbContext db, ILogger<NotificationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Returns a paginated list of notifications for the user, ordered by creation date
        /// descending. Includes total count, unread count, and a <c>HasMore</c> flag.
        /// </summary>
        public async Task<NotificationPageDto> GetNotificationsAsync(Guid userId, int page = 1, int pageSize = 20, CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var query = _db.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt);

            var totalCount = await query.CountAsync(ct);
            var unreadCount = await query.CountAsync(n => !n.IsRead, ct);

            var notifications = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Type = n.Type.ToString().ToLowerInvariant(),
                    ActorId = n.ActorId,
                    ActorUsername = n.Actor != null ? n.Actor.Username : null,
                    ActorDisplayName = n.Actor != null ? n.Actor.DisplayName : null,
                    ActorProfileImageUrl = n.Actor != null ? n.Actor.ProfileImageUrl : null,
                    RecipeId = n.RecipeId,
                    RecipeTitle = n.Recipe != null ? n.Recipe.Title : null,
                    RecipeImageUrl = n.Recipe != null ? n.Recipe.ImageUrl : null,
                    CommentId = n.CommentId,
                    CommentPreview = n.Comment != null ? (n.Comment.Content.Length > 100 ? n.Comment.Content.Substring(0, 100) + "..." : n.Comment.Content) : null,
                    CookbookId = n.CookbookId,
                    CookbookTitle = n.Cookbook != null ? n.Cookbook.Title : null,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                })
                .ToArrayAsync(ct);

            return new NotificationPageDto
            {
                Notifications = notifications,
                TotalCount = totalCount,
                UnreadCount = unreadCount,
                Page = page,
                PageSize = pageSize,
                HasMore = (page * pageSize) < totalCount
            };
        }

        /// <summary>Returns the count of unread notifications for the user.</summary>
        public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default)
        {
            return await _db.Notifications
                .CountAsync(n => n.UserId == userId && !n.IsRead, ct);
        }

        /// <summary>
        /// Marks a single notification as read. Returns <c>false</c> when the notification does not
        /// exist or does not belong to <paramref name="userId"/>.
        /// </summary>
        public async Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default)
        {
            var notification = await _db.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);

            if (notification == null)
                return false;

            notification.IsRead = true;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>
        /// Bulk-marks all unread notifications for the user as read using a single
        /// <c>ExecuteUpdateAsync</c> call. Returns the number of rows affected.
        /// </summary>
        public async Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
        {
            return await _db.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
        }

        /// <summary>
        /// Hard-deletes a notification. Returns <c>false</c> when the notification does not exist
        /// or does not belong to <paramref name="userId"/>.
        /// </summary>
        public async Task<bool> DeleteNotificationAsync(Guid notificationId, Guid userId, CancellationToken ct = default)
        {
            var notification = await _db.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);

            if (notification == null)
                return false;

            _db.Notifications.Remove(notification);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>
        /// Core notification creation method. Skips creation when <paramref name="userId"/> ==
        /// <paramref name="actorId"/> to suppress self-notifications. DB save failures are caught
        /// and logged — notification errors never bubble up to callers.
        /// </summary>
        public async Task CreateNotificationAsync(NotificationType type, Guid userId, Guid actorId, Guid? recipeId = null, Guid? commentId = null, Guid? cookbookId = null, CancellationToken ct = default)
        {
            // Don't create notification if user is acting on their own content
            if (userId == actorId)
                return;

            var notification = new Notification
            {
                Type = type,
                UserId = userId,
                ActorId = actorId,
                RecipeId = recipeId,
                CommentId = commentId,
                CookbookId = cookbookId
            };

            _db.Notifications.Add(notification);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create notification of type {Type} for user {UserId}", type, userId);
            }
        }

        public async Task NotifyRecipeLikeAsync(Guid recipeOwnerId, Guid actorId, Guid recipeId, CancellationToken ct = default)
        {
            await CreateNotificationAsync(NotificationType.Like, recipeOwnerId, actorId, recipeId, null, null, ct);
        }

        public async Task NotifyRecipeCommentAsync(Guid recipeOwnerId, Guid actorId, Guid recipeId, Guid commentId, CancellationToken ct = default)
        {
            await CreateNotificationAsync(NotificationType.Comment, recipeOwnerId, actorId, recipeId, commentId, null, ct);
        }

        public async Task NotifyCommentReplyAsync(Guid commentOwnerId, Guid actorId, Guid recipeId, Guid commentId, CancellationToken ct = default)
        {
            await CreateNotificationAsync(NotificationType.Reply, commentOwnerId, actorId, recipeId, commentId, null, ct);
        }

        public async Task NotifyFollowAsync(Guid followedUserId, Guid followerId, CancellationToken ct = default)
        {
            await CreateNotificationAsync(NotificationType.Follow, followedUserId, followerId, null, null, null, ct);
        }

        public async Task NotifyRepostAsync(Guid recipeOwnerId, Guid actorId, Guid recipeId, CancellationToken ct = default)
        {
            await CreateNotificationAsync(NotificationType.Repost, recipeOwnerId, actorId, recipeId, null, null, ct);
        }

        public async Task NotifyMentionsAsync(IEnumerable<Guid> mentionedUserIds, Guid actorId, Guid recipeId, Guid commentId, CancellationToken ct = default)
        {
            foreach (var userId in mentionedUserIds)
            {
                await CreateNotificationAsync(NotificationType.Mention, userId, actorId, recipeId, commentId, null, ct);
            }
        }

        public async Task NotifyCookbookInviteAsync(Guid invitedUserId, Guid ownerId, Guid cookbookId, CancellationToken ct = default)
        {
            await CreateNotificationAsync(NotificationType.CookbookInvite, invitedUserId, ownerId, null, null, cookbookId, ct);
        }

        public async Task NotifyCookbookRecipeRequestAsync(Guid ownerId, Guid requesterId, Guid cookbookId, Guid recipeId, CancellationToken ct = default)
        {
            await CreateNotificationAsync(NotificationType.CookbookRecipeRequest, ownerId, requesterId, recipeId, null, cookbookId, ct);
        }

        public async Task NotifyCookbookRequestApprovedAsync(Guid requesterId, Guid ownerId, Guid cookbookId, Guid recipeId, CancellationToken ct = default)
        {
            await CreateNotificationAsync(NotificationType.CookbookRequestApproved, requesterId, ownerId, recipeId, null, cookbookId, ct);
        }

        public async Task NotifyCookbookRequestRejectedAsync(Guid requesterId, Guid ownerId, Guid cookbookId, Guid recipeId, CancellationToken ct = default)
        {
            await CreateNotificationAsync(NotificationType.CookbookRequestRejected, requesterId, ownerId, recipeId, null, cookbookId, ct);
        }

        /// <summary>
        /// Creates a <see cref="NotificationType.RecipeTrending"/> notification for the recipe owner.
        /// Skips creation if a trending notification for the same recipe was already created within
        /// the last 24 hours, preventing notification spam for persistently trending recipes.
        /// Uses a system-actor pattern (actor == owner) to bypass self-notification suppression.
        /// </summary>
        public async Task NotifyRecipeTrendingAsync(Guid recipeOwnerId, Guid recipeId, int rank, CancellationToken ct = default)
        {
            // Deduplicate: only notify once per recipe per 24 hours
            var recentNotification = await _db.Notifications
                .AnyAsync(n =>
                    n.UserId == recipeOwnerId &&
                    n.RecipeId == recipeId &&
                    n.Type == NotificationType.RecipeTrending &&
                    n.CreatedAt > DateTime.UtcNow.AddHours(-24), ct);

            if (recentNotification)
            {
                _logger.LogDebug("Trending notification skipped for recipe {RecipeId} - already notified within 24h", recipeId);
                return;
            }

            // System notification - create directly to bypass self-notification check
            var notification = new Notification
            {
                Type = NotificationType.RecipeTrending,
                UserId = recipeOwnerId,
                ActorId = recipeOwnerId, // Self-actor for system notifications
                RecipeId = recipeId
            };

            _db.Notifications.Add(notification);

            try
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Trending notification created for recipe {RecipeId} at rank {Rank}", recipeId, rank);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create trending notification for recipe {RecipeId}", recipeId);
            }
        }
    }
}
