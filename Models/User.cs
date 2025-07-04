using System;
using System.Collections.Generic;

namespace Platerly.Api.Models
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid(); // Unique user ID

        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public string? DisplayName { get; set; }
        public string? Bio { get; set; }

        public string? ProfileImageUrl { get; set; }

        public DateTime CreatedAt { get; set; }

        // Navigation properties (relationships)
        public ICollection<Recipe>? Recipes { get; set; }
        public ICollection<Cookbook>? Cookbooks { get; set; }

        // Follow / Following  (relationships)
        public ICollection<UserFollow>? Followers { get; set; } // People following this user
        public ICollection<UserFollow>? Following { get; set; } // People this user follows

        // user allergy
        public List<string> AllergyKeywords { get; set; } = new List<string>();

        public ICollection<UserAllergy> Allergies { get; set; } = new List<UserAllergy>();


    }
}
