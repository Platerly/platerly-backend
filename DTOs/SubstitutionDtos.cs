namespace Platerly.Api.DTOs
{
    // Request to suggest a new substitution
    public class SuggestSubstitutionDto
    {
        public Guid OriginalIngredientId { get; set; }
        public Guid SubstituteIngredientId { get; set; }
        public string Reason { get; set; } = "Allergy"; // Allergy, DietaryPreference, Availability, etc.
        public Guid? AllergyId { get; set; }
        public decimal ConversionRatio { get; set; } = 1.0m;
        public string? Notes { get; set; }
    }

    // Response for a substitution
    public class SubstitutionDto
    {
        public Guid Id { get; set; }
        public Guid OriginalIngredientId { get; set; }
        public string OriginalIngredientName { get; set; } = string.Empty;
        public Guid SubstituteIngredientId { get; set; }
        public string SubstituteIngredientName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public Guid? AllergyId { get; set; }
        public string? AllergyName { get; set; }
        public decimal ConversionRatio { get; set; }
        public string? Notes { get; set; }
        public int ConfidenceScore { get; set; }
        public int PositiveVotes { get; set; }
        public int NegativeVotes { get; set; }
        public int UsageCount { get; set; }
        public bool IsVerified { get; set; }
        public Guid? SuggestedByUserId { get; set; }
        public string? SuggestedByUsername { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // Request to vote on a substitution
    public class VoteSubstitutionDto
    {
        public bool IsPositive { get; set; }
        public string? Comment { get; set; }
    }

    // Response for substitutions grouped by allergy
    public class AllergySubstitutionsDto
    {
        public Guid AllergyId { get; set; }
        public string AllergyName { get; set; } = string.Empty;
        public List<SubstitutionDto> Substitutions { get; set; } = new();
    }

    // Request to find substitutions for multiple ingredients
    public class BulkSubstitutionRequestDto
    {
        public List<string> IngredientNames { get; set; } = new();
        public List<Guid>? UserAllergyIds { get; set; }
    }

    // Response for bulk substitution lookup
    public class BulkSubstitutionResponseDto
    {
        public string OriginalIngredient { get; set; } = string.Empty;
        public bool HasAllergen { get; set; }
        public List<string> TriggeredAllergies { get; set; } = new();
        public List<SubstitutionDto> Substitutions { get; set; } = new();
    }
}
