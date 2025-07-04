using System;

namespace Platerly.Api.Models
{
    public class Comment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Foreign Keys
        public Guid UserId { get; set; }
        public User? User { get; set; }

        public Guid RecipeId { get; set; }
        public Recipe? Recipe { get; set; }
    }
}
