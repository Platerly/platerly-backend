using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Categories of prohibited ingredients
    /// </summary>
    public enum ProhibitedCategory
    {
        Haram = 0,           // Pork, alcohol, etc.
        Dangerous = 1,       // Chemicals, poisons, non-food items
        Illegal = 2,         // Drugs, controlled substances
        Harmful = 3,         // Items that could cause harm if consumed
        Offensive = 4        // Inappropriate/offensive content
    }

    /// <summary>
    /// Tracks ingredients that are banned from the platform.
    /// Used to prevent users from adding harmful, haram, or illegal ingredients.
    /// </summary>
    public class ProhibitedIngredient
    {
        public int Id { get; set; }

        /// <summary>
        /// The keyword or phrase to match against (case-insensitive).
        /// Can be exact match or partial match based on MatchType.
        /// </summary>
        [Required, MaxLength(256)]
        public string Keyword { get; set; } = string.Empty;

        /// <summary>
        /// Category of prohibition
        /// </summary>
        public ProhibitedCategory Category { get; set; }

        /// <summary>
        /// How to match this keyword
        /// </summary>
        public MatchType MatchType { get; set; } = MatchType.Contains;

        /// <summary>
        /// Human-readable reason for the ban (shown to users)
        /// </summary>
        [MaxLength(512)]
        public string? Reason { get; set; }

        /// <summary>
        /// Internal notes for admins
        /// </summary>
        [MaxLength(1024)]
        public string? AdminNotes { get; set; }

        /// <summary>
        /// Whether this rule is active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When this rule was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Who created this rule (admin user ID)
        /// </summary>
        public Guid? CreatedByUserId { get; set; }

        /// <summary>
        /// Severity level - determines action taken
        /// </summary>
        public ViolationSeverity Severity { get; set; } = ViolationSeverity.Block;
    }

    public enum MatchType
    {
        Exact = 0,           // Must match exactly
        Contains = 1,        // Keyword appears anywhere in ingredient name
        StartsWith = 2,      // Ingredient starts with keyword
        Regex = 3            // Regular expression match
    }

    public enum ViolationSeverity
    {
        Warn = 0,            // Show warning but allow
        Block = 1,           // Block submission
        BlockAndFlag = 2,    // Block and flag user for review
        BlockAndSuspend = 3  // Block and auto-suspend user
    }
}
