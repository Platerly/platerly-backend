namespace Platerly.Api.DTOs
{
    public sealed class IngredientAlertDetailDto
    {
        public Guid IngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public string Source { get; set; } = "Global"; // "Global" or "Override"
        public string SeverityResolved { get; set; } = "Low"; // Low | Medium | High
        public bool MayContain { get; set; }
        public bool CrossContactRisk { get; set; }
        public bool ProcessingAid { get; set; }
        public bool IsTraceOnly { get; set; } // true if alert arises only from trace flags
    }

    public sealed class AllergyAlertDto
    {
        public string AllergenType { get; set; } = string.Empty; // e.g., Nickel | Gluten | Sulfites
        public string HighestSeverity { get; set; } = "Low";     // aggregate across ingredients
        public List<IngredientAlertDetailDto> Ingredients { get; set; } = new();
    }

    public sealed class RecipeAlertsResponse
    {
        public Guid RecipeId { get; set; }
        public List<AllergyAlertDto> Alerts { get; set; } = new();
    }
}
