namespace Platerly.Api.DTOs
{
    // ========== Regional Demand Forecast ==========

    public class RegionalDemandForecastDto
    {
        public Guid GlobalIngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string Region { get; set; } = string.Empty;
        public int CurrentPeriodUsage { get; set; }
        public int PreviousPeriodUsage { get; set; }
        public decimal GrowthRate { get; set; }
        public decimal Velocity { get; set; }
        public string Trend { get; set; } = string.Empty; // Rising, Stable, Declining
    }

    public class RegionalDemandForecastRequest
    {
        public string? Region { get; set; }
        public int Days { get; set; } = 30;
        public int Take { get; set; } = 50;
        public string? Category { get; set; }
    }

    public class RegionalDemandForecastResponse
    {
        public string? Region { get; set; }
        public int AnalysisDays { get; set; }
        public List<RegionalDemandForecastDto> Forecasts { get; set; } = new();
        public int TotalIngredients { get; set; }
    }

    // ========== Regional Comparison ==========

    public class RegionalComparisonIngredientDto
    {
        public Guid GlobalIngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public Dictionary<string, long> UsageByRegion { get; set; } = new();
        public Dictionary<string, int> RankByRegion { get; set; } = new();
    }

    public class RegionalComparisonRequest
    {
        public string Regions { get; set; } = string.Empty;
        public Guid? IngredientId { get; set; }
        public int Take { get; set; } = 20;
    }

    public class RegionalComparisonResponse
    {
        public List<string> Regions { get; set; } = new();
        public List<RegionalComparisonIngredientDto> Ingredients { get; set; } = new();
    }
}
