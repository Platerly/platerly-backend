using System;

namespace Platerly.Api.Models
{
    public enum ContributionStatus { Pending = 0, Approved = 1, Rejected = 2 }

    public class CookbookContributionRequest
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CookbookId { get; set; }
        public Cookbook Cookbook { get; set; } = null!;

        public Guid RecipeId { get; set; }
        public Recipe Recipe { get; set; } = null!;

        public Guid RequestedByUserId { get; set; }
        public User? RequestedByUser { get; set; }

        public string? Message { get; set; }            // optional note with request
        public ContributionStatus Status { get; set; } = ContributionStatus.Pending;

        public Guid? ReviewedByUserId { get; set; }     // owner who acted
        public User? ReviewedByUser { get; set; }
        public DateTime? ReviewedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
