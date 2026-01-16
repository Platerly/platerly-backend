using System;

namespace Platerly.Api.DTOs
{
    public class NotificationDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;  // like, comment, reply, follow, mention, repost

        // Actor (user who triggered the notification)
        public Guid ActorId { get; set; }
        public string? ActorUsername { get; set; }
        public string? ActorDisplayName { get; set; }
        public string? ActorProfileImageUrl { get; set; }

        // Optional recipe context
        public Guid? RecipeId { get; set; }
        public string? RecipeTitle { get; set; }
        public string? RecipeImageUrl { get; set; }

        // Optional comment context
        public Guid? CommentId { get; set; }
        public string? CommentPreview { get; set; }  // Truncated comment content

        // Optional cookbook context
        public Guid? CookbookId { get; set; }
        public string? CookbookTitle { get; set; }

        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class NotificationPageDto
    {
        public NotificationDto[] Notifications { get; set; } = Array.Empty<NotificationDto>();
        public int TotalCount { get; set; }
        public int UnreadCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public bool HasMore { get; set; }
    }
}
