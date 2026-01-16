using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Notifications
{
    public interface INotificationService
    {
        /// <summary>
        /// Get paginated notifications for a user
        /// </summary>
        Task<NotificationPageDto> GetNotificationsAsync(Guid userId, int page = 1, int pageSize = 20, CancellationToken ct = default);

        /// <summary>
        /// Get unread notification count for a user
        /// </summary>
        Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);

        /// <summary>
        /// Mark a single notification as read
        /// </summary>
        Task<bool> MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default);

        /// <summary>
        /// Mark all notifications as read for a user
        /// </summary>
        Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken ct = default);

        /// <summary>
        /// Delete a notification
        /// </summary>
        Task<bool> DeleteNotificationAsync(Guid notificationId, Guid userId, CancellationToken ct = default);

        /// <summary>
        /// Create a notification (used internally by event triggers)
        /// </summary>
        Task CreateNotificationAsync(NotificationType type, Guid userId, Guid actorId, Guid? recipeId = null, Guid? commentId = null, Guid? cookbookId = null, CancellationToken ct = default);

        /// <summary>
        /// Notify when someone likes a recipe
        /// </summary>
        Task NotifyRecipeLikeAsync(Guid recipeOwnerId, Guid actorId, Guid recipeId, CancellationToken ct = default);

        /// <summary>
        /// Notify when someone comments on a recipe
        /// </summary>
        Task NotifyRecipeCommentAsync(Guid recipeOwnerId, Guid actorId, Guid recipeId, Guid commentId, CancellationToken ct = default);

        /// <summary>
        /// Notify when someone replies to a comment
        /// </summary>
        Task NotifyCommentReplyAsync(Guid commentOwnerId, Guid actorId, Guid recipeId, Guid commentId, CancellationToken ct = default);

        /// <summary>
        /// Notify when someone follows a user
        /// </summary>
        Task NotifyFollowAsync(Guid followedUserId, Guid followerId, CancellationToken ct = default);

        /// <summary>
        /// Notify when someone reposts a recipe
        /// </summary>
        Task NotifyRepostAsync(Guid recipeOwnerId, Guid actorId, Guid recipeId, CancellationToken ct = default);

        /// <summary>
        /// Notify users mentioned in a comment
        /// </summary>
        Task NotifyMentionsAsync(IEnumerable<Guid> mentionedUserIds, Guid actorId, Guid recipeId, Guid commentId, CancellationToken ct = default);

        /// <summary>
        /// Notify when someone is added as a cookbook contributor
        /// </summary>
        Task NotifyCookbookInviteAsync(Guid invitedUserId, Guid ownerId, Guid cookbookId, CancellationToken ct = default);

        /// <summary>
        /// Notify cookbook owner when someone requests to add a recipe
        /// </summary>
        Task NotifyCookbookRecipeRequestAsync(Guid ownerId, Guid requesterId, Guid cookbookId, Guid recipeId, CancellationToken ct = default);

        /// <summary>
        /// Notify requester when their cookbook contribution request is approved
        /// </summary>
        Task NotifyCookbookRequestApprovedAsync(Guid requesterId, Guid ownerId, Guid cookbookId, Guid recipeId, CancellationToken ct = default);

        /// <summary>
        /// Notify requester when their cookbook contribution request is rejected
        /// </summary>
        Task NotifyCookbookRequestRejectedAsync(Guid requesterId, Guid ownerId, Guid cookbookId, Guid recipeId, CancellationToken ct = default);

        /// <summary>
        /// Notify recipe owner when their recipe starts trending (system notification)
        /// </summary>
        Task NotifyRecipeTrendingAsync(Guid recipeOwnerId, Guid recipeId, int rank, CancellationToken ct = default);
    }
}
