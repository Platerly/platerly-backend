using System;

namespace Platerly.Api.Models
{
    public class CommentLike
    {
        public Guid UserId { get; set; }
        public User? User { get; set; }

        public Guid CommentId { get; set; }
        public Comment? Comment { get; set; }

        public DateTime LikedAt { get; set; } = DateTime.UtcNow;
    }
}
