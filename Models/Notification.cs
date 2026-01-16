using System;

namespace Platerly.Api.Models
{
    public enum NotificationType
    {
        Like,                   // Someone liked your recipe
        Comment,                // Someone commented on your recipe
        Reply,                  // Someone replied to your comment
        Follow,                 // Someone followed you
        Mention,                // Someone mentioned you in a comment
        Repost,                 // Someone reposted your recipe
        CookbookInvite,         // You were added as a cookbook contributor
        CookbookRecipeRequest,  // Someone requested to add a recipe to your cookbook
        CookbookRequestApproved,// Your cookbook contribution request was approved
        CookbookRequestRejected,// Your cookbook contribution request was rejected
        RecipeTrending          // Your recipe is now trending (system notification)
    }

    public class Notification
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public NotificationType Type { get; set; }

        // The user who receives this notification
        public Guid UserId { get; set; }
        public User? User { get; set; }

        // The user who triggered this notification (actor)
        public Guid ActorId { get; set; }
        public User? Actor { get; set; }

        // Optional: Associated recipe (for like, comment, repost, mention)
        public Guid? RecipeId { get; set; }
        public Recipe? Recipe { get; set; }

        // Optional: Associated comment (for reply, mention)
        public Guid? CommentId { get; set; }
        public Comment? Comment { get; set; }

        // Optional: Associated cookbook (for cookbook-related notifications)
        public Guid? CookbookId { get; set; }
        public Cookbook? Cookbook { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
