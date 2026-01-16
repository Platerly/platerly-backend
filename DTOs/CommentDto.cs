using System;
using System.Collections.Generic;

namespace Platerly.Api.DTOs
{
    public class CommentDto
    {
        public Guid Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public Guid UserId { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public string? ProfileImageUrl { get; set; }

        // Reply support
        public Guid? ParentCommentId { get; set; }

        // Likes
        public int LikesCount { get; set; }
        public bool IsLiked { get; set; }

        // Replies (only populated for top-level comments)
        public int RepliesCount { get; set; }
        public List<CommentDto>? Replies { get; set; }
    }

    public class UpdateCommentDto
    {
        public string Content { get; set; } = string.Empty;
    }

    public class CreateReplyDto
    {
        public string Content { get; set; } = string.Empty;
    }
}
