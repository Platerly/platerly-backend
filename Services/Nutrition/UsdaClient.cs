using System.Text.Json;
using System.Text.Json.Serialization;

namespace Platerly.Api.Services.Nutrition
{
    /// <summary>
    /// HTTP client for USDA FoodData Central API (free, key-based).
    /// https://fdc.nal.usda.gov/api-guide.html
    /// </summary>
    public class UsdaClient
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly ILogger<UsdaClient> _logger;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public UsdaClient(HttpClient http, IConfiguration config, ILogger<UsdaClient> logger)
        {
            _http = http;
            _apiKey = config["Usda:ApiKey"] ?? "DEMO_KEY";
            _logger = logger;
            _http.BaseAddress = new Uri("https://api.nal.usda.gov/fdc/v1/");
        }

        /// <summary>
        /// Search for a food item by name and return the best match with nutrients.
        /// </summary>
        public async Task<UsdaFoodMatch?> SearchFoodAsync(string query, CancellationToken ct = default)
        {
            try
            {
                var url = $"foods/search?api_key={_apiKey}&query={Uri.EscapeDataString(query)}&pageSize=1&dataType=Foundation,SR%20Legacy";
                var response = await _http.GetAsync(url, ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("USDA API returned {StatusCode} for query '{Query}'",
                        response.StatusCode, query);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<UsdaSearchResult>(json, JsonOptions);

                var food = result?.Foods?.FirstOrDefault();
                if (food == null) return null;

                return new UsdaFoodMatch
                {
                    FdcId = food.FdcId,
                    Description = food.Description ?? query,
                    CaloriesPer100g = GetNutrient(food, 1008), // Energy (kcal)
                    ProteinPer100g = GetNutrient(food, 1003),  // Protein
                    CarbsPer100g = GetNutrient(food, 1005),    // Carbohydrate
                    FatPer100g = GetNutrient(food, 1004),      // Total lipid (fat)
                    FiberPer100g = GetNutrient(food, 1079),    // Fiber
                    SodiumMgPer100g = GetNutrient(food, 1093), // Sodium
                    SugarPer100g = GetNutrient(food, 2000),    // Sugars, total
                    SaturatedFatPer100g = GetNutrient(food, 1258), // Fatty acids, saturated
                    CholesterolMgPer100g = GetNutrient(food, 1253) // Cholesterol
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching USDA for '{Query}'", query);
                return null;
            }
        }

        private static decimal GetNutrient(UsdaFood food, int nutrientId)
        {
            var nutrient = food.FoodNutrients?.FirstOrDefault(n => n.NutrientId == nutrientId);
            return (decimal)(nutrient?.Value ?? 0);
        }

        // ========== USDA API response models ==========

        public class UsdaSearchResult
        {
            public List<UsdaFood>? Foods { get; set; }
            public int TotalHits { get; set; }
        }

        public class UsdaFood
        {
            public int FdcId { get; set; }
            public string? Description { get; set; }
            public List<UsdaFoodNutrient>? FoodNutrients { get; set; }
        }

        public class UsdaFoodNutrient
        {
            public int NutrientId { get; set; }
            public string? NutrientName { get; set; }
            public double Value { get; set; }
            public string? UnitName { get; set; }
        }

        public class UsdaFoodMatch
        {
            public int FdcId { get; set; }
            public string Description { get; set; } = string.Empty;
            public decimal CaloriesPer100g { get; set; }
            public decimal ProteinPer100g { get; set; }
            public decimal CarbsPer100g { get; set; }
            public decimal FatPer100g { get; set; }
            public decimal FiberPer100g { get; set; }
            public decimal SodiumMgPer100g { get; set; }
            public decimal SugarPer100g { get; set; }
            public decimal SaturatedFatPer100g { get; set; }
            public decimal CholesterolMgPer100g { get; set; }
        }
    }
}
