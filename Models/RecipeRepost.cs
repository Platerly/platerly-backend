using System;

namespace Platerly.Api.Models
{
    public class RecipeRepost
    {
        public Guid UserId { get; set; }
        public User? User { get; set; }

        public Guid RecipeId { get; set; }
        public Recipe? Recipe { get; set; }

        public DateTime RepostedAt { get; set; } = DateTime.UtcNow;
    }
}
