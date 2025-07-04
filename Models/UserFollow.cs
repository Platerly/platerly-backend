using System;

namespace Platerly.Api.Models
{
    public class UserFollow
    {
        public Guid FollowerId { get; set; }      // The user who follows
        public Guid FollowingId { get; set; }     // The user being followed

        public User? Follower { get; set; }
        public User? Following { get; set; }

        public DateTime FollowedAt { get; set; } = DateTime.UtcNow;
    }
}
