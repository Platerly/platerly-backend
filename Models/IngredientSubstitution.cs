using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Reason for the substitution
    /// </summary>
    public enum SubstitutionReason
    {
        Allergy = 0,              // User has allergy
        DietaryPreference = 1,    // Vegan, keto, etc.
        Availability = 2,         // Ingredient not available in region
        Cost = 3,                 // Cheaper alternative
        Taste = 4,                // Flavor preference
        Health = 5,               // Health/nutrition reasons
        Religious = 6,            // Halal, kosher, etc.
        Other = 7
    }

    /// <summary>
    /// Links ingredients that can substitute for each other.
    /// Crowdsourced and validated through user feedback.
    /// </summary>
    public class IngredientSubstitution
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The original ingredient being replaced
        /// </summary>
        public Guid OriginalIngredientId { get; set; }
        public GlobalIngredient OriginalIngredient { get; set; } = null!;

        /// <summary>
        /// The substitute ingredient
        /// </summary>
        public Guid SubstituteIngredientId { get; set; }
        public GlobalIngredient SubstituteIngredient { get; set; } = null!;

        /// <summary>
        /// Primary reason for this substitution
        /// </summary>
        public SubstitutionReason Reason { get; set; }

        /// <summary>
        /// Specific allergy this substitution addresses (if Reason = Allergy)
        /// </summary>
        public Guid? AllergyId { get; set; }
        public Allergy? Allergy { get; set; }

        /// <summary>
        /// Conversion ratio (e.g., 1.0 = 1:1, 0.5 = use half as much)
        /// </summary>
        public decimal ConversionRatio { get; set; } = 1.0m;

        /// <summary>
        /// Notes about the substitution (e.g., "texture will be slightly different")
        /// </summary>
        [MaxLength(512)]
        public string? Notes { get; set; }

        /// <summary>
        /// Confidence score 0-100 based on user validation
        /// </summary>
        public int ConfidenceScore { get; set; } = 50;

        /// <summary>
        /// Number of times users have validated this works
        /// </summary>
        public int PositiveVotes { get; set; } = 0;

        /// <summary>
        /// Number of times users said this doesn't work
        /// </summary>
        public int NegativeVotes { get; set; } = 0;

        /// <summary>
        /// Number of times this substitution was used
        /// </summary>
        public int UsageCount { get; set; } = 0;

        /// <summary>
        /// Who suggested this substitution (null = system/AI generated)
        /// </summary>
        public Guid? SuggestedByUserId { get; set; }
        public User? SuggestedByUser { get; set; }

        /// <summary>
        /// Whether this is verified by admin/expert
        /// </summary>
        public bool IsVerified { get; set; } = false;

        /// <summary>
        /// Whether this substitution is active
        /// </summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Tracks user votes on substitution effectiveness
    /// </summary>
    public class SubstitutionVote
    {
        public Guid UserId { get; set; }
        public User User { get; set; } = null!;

        public Guid SubstitutionId { get; set; }
        public IngredientSubstitution Substitution { get; set; } = null!;

        /// <summary>
        /// True = worked well, False = didn't work
        /// </summary>
        public bool IsPositive { get; set; }

        /// <summary>
        /// Optional feedback
        /// </summary>
        [MaxLength(256)]
        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
