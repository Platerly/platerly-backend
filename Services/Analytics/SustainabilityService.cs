using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    public class SustainabilityService : ISustainabilityService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<SustainabilityService> _logger;

        public SustainabilityService(
            PlaterlyDbContext db,
            ILogger<SustainabilityService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<RecipeSustainabilityResponse?> GetRecipeSustainabilityAsync(
            Guid recipeId,
            CancellationToken ct = default)
        {
            var recipe = await _db.Recipes
                .Include(r => r.Ingredients!)
                    .ThenInclude(i => i.GlobalIngredient)
                .Where(r => r.Id == recipeId && !r.IsDeleted)
                .FirstOrDefaultAsync(ct);

            if (recipe == null) return null;

            var ingredients = recipe.Ingredients?.ToList() ?? new();
            var ingredientResults = new List<IngredientSustainabilityDto>();
            decimal totalCarbon = 0;
            decimal totalWater = 0;
            int seasonalCount = 0;
            int matched = 0;
            int totalScore = 0;

            var currentMonth = DateTime.UtcNow.Month;

            foreach (var ingredient in ingredients)
            {
                var globalId = ingredient.GlobalIngredientId;
                var name = ingredient.GlobalIngredient?.Name ?? "Unknown";

                var data = await _db.IngredientSustainabilityData
                    .Where(s => s.GlobalIngredientId == globalId)
                    .FirstOrDefaultAsync(ct);

                if (data != null)
                {
                    matched++;
                    totalCarbon += data.CarbonScorePer100g;
                    totalWater += data.WaterUsagePer100g;
                    totalScore += data.OverallScore;

                    var isSeasonal = IsInSeason(data.SeasonMonths, currentMonth);
                    if (isSeasonal) seasonalCount++;

                    ingredientResults.Add(new IngredientSustainabilityDto
                    {
                        IngredientName = name,
                        OverallScore = data.OverallScore,
                        CarbonScorePer100g = data.CarbonScorePer100g,
                        WaterUsagePer100g = data.WaterUsagePer100g,
                        IsSeasonal = isSeasonal,
                        TransportScore = data.TransportScore,
                        Matched = true
                    });
                }
                else
                {
                    ingredientResults.Add(new IngredientSustainabilityDto
                    {
                        IngredientName = name,
                        OverallScore = 50, // Default neutral
                        Matched = false
                    });
                }
            }

            var avgScore = matched > 0 ? totalScore / matched : 50;
            var rating = avgScore switch
            {
                >= 80 => "A",
                >= 60 => "B",
                >= 40 => "C",
                >= 20 => "D",
                _ => "E"
            };

            return new RecipeSustainabilityResponse
            {
                RecipeId = recipeId,
                RecipeTitle = recipe.Title,
                OverallScore = avgScore,
                Rating = rating,
                TotalCarbonKg = Math.Round(totalCarbon / 1000m, 3), // Convert to kg
                TotalWaterLiters = Math.Round(totalWater, 1),
                SeasonalIngredients = seasonalCount,
                TotalIngredients = ingredients.Count,
                MatchedIngredients = matched,
                Ingredients = ingredientResults
            };
        }

        public async Task<SustainabilityCompareResponse> CompareRecipesAsync(
            SustainabilityCompareRequest request,
            CancellationToken ct = default)
        {
            var results = new List<SustainabilityCompareItemDto>();

            foreach (var recipeId in request.RecipeIds)
            {
                var detail = await GetRecipeSustainabilityAsync(recipeId, ct);
                if (detail != null)
                {
                    results.Add(new SustainabilityCompareItemDto
                    {
                        RecipeId = detail.RecipeId,
                        RecipeTitle = detail.RecipeTitle,
                        OverallScore = detail.OverallScore,
                        Rating = detail.Rating,
                        TotalCarbonKg = detail.TotalCarbonKg,
                        TotalWaterLiters = detail.TotalWaterLiters
                    });
                }
            }

            return new SustainabilityCompareResponse
            {
                Recipes = results.OrderByDescending(r => r.OverallScore).ToList()
            };
        }

        private static bool IsInSeason(string? seasonMonths, int currentMonth)
        {
            if (string.IsNullOrEmpty(seasonMonths)) return true; // No data = assume available
            var months = seasonMonths.Split(',', StringSplitOptions.RemoveEmptyEntries);
            return months.Any(m => int.TryParse(m.Trim(), out var month) && month == currentMonth);
        }
    }
}
