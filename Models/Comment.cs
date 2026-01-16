using System;
using System.Collections.Generic;

namespace Platerly.Api.Models
{
    public class Comment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Foreign Keys
        public Guid UserId { get; set; }
        public User? User { get; set; }

        public Guid RecipeId { get; set; }
        public Recipe? Recipe { get; set; }

        // Reply support (parent comment, null for top-level comments)
        public Guid? ParentCommentId { get; set; }
        public Comment? ParentComment { get; set; }

        // Navigation properties
        public ICollection<Comment> Replies { get; set; } = new List<Comment>();
        public ICollection<CommentLike> Likes { get; set; } = new List<CommentLike>();
    }
}
