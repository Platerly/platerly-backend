using System;
using System.Collections.Generic;

namespace Platerly.Api.Models
{
    public class Cookbook
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }            // owner
        public User? User { get; set; }

        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }     // short blurb under title

        public bool IsPublic { get; set; } = false;

        public string? CoverImageUrl { get; set; }
        public string? CoverImageBlobName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // optimistic concurrency
        public byte[]? RowVersion { get; set; }

        public ICollection<CookbookRecipe> Recipes { get; set; } = new List<CookbookRecipe>();
        public ICollection<CookbookContributor> Contributors { get; set; } = new List<CookbookContributor>();

    }
}
