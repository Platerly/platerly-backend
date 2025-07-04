using System;
using System.Collections.Generic;

namespace Platerly.Api.Models
{
    public class Cookbook
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public bool IsPublic { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Foreign Key
        public Guid UserId { get; set; }

        // Navigation
        public User? User { get; set; }

        public ICollection<CookbookRecipe> Recipes { get; set; } = new List<CookbookRecipe>();

    }
}
