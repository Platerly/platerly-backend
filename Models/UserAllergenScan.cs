using System;

namespace Platerly.Api.Models
{
    public class UserAllergenScan
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }
        public Guid RecipeId { get; set; }  // No FK — survives recipe deletion
        public DateTime WeekStartDate { get; set; } // Monday midnight UTC (date only)
        public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public User User { get; set; } = null!;
    }
}
