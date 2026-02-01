namespace Platerly.Api.DTOs
{
    // ========== Top Substitutions ==========

    public class TopSubstitutionDto
    {
        public Guid SubstitutionId { get; set; }
        public Guid OriginalIngredientId { get; set; }
        public string OriginalIngredientName { get; set; } = string.Empty;
        public Guid SubstituteIngredientId { get; set; }
        public string SubstituteIngredientName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string? AllergyName { get; set; }
        public decimal ConversionRatio { get; set; }
        public int ConfidenceScore { get; set; }
        public int PositiveVotes { get; set; }
        public int NegativeVotes { get; set; }
        public int UsageCount { get; set; }
        public bool IsVerified { get; set; }
        public string? Notes { get; set; }
    }

    public class TopSubstitutionsRequest
    {
        public string? Reason { get; set; }
        public Guid? AllergyId { get; set; }
        public string? Region { get; set; }
        public int Take { get; set; } = 20;
    }

    public class TopSubstitutionsResponse
    {
        public List<TopSubstitutionDto> Substitutions { get; set; } = new();
        public int TotalSubstitutions { get; set; }
        public string? ReasonFilter { get; set; }
    }

    // ========== Substitutions for Ingredient ==========

    public class IngredientSubstitutionsRequest
    {
        public Guid IngredientId { get; set; }
        public string? Reason { get; set; }
        public int Take { get; set; } = 50;
    }

    public class IngredientSubstitutionsResponse
    {
        public Guid IngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public List<TopSubstitutionDto> Substitutions { get; set; } = new();
        public int TotalSubstitutions { get; set; }
    }

    // ========== Substitution Reason Breakdown ==========

    public class SubstitutionReasonDto
    {
        public string Reason { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Percentage { get; set; }
        public int VerifiedCount { get; set; }
        public double AverageConfidenceScore { get; set; }
    }

    public class SubstitutionReasonBreakdownResponse
    {
        public List<SubstitutionReasonDto> Reasons { get; set; } = new();
        public int TotalSubstitutions { get; set; }
    }
}
