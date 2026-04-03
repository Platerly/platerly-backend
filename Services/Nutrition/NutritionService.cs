using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Nutrition
{
    /// <summary>
    /// B2C nutrition layer — computes per-ingredient and total recipe nutrition using a DB cache
    /// backed by the USDA FoodData Central API.
    /// Depends on <see cref="PlaterlyDbContext"/> and <see cref="UsdaClient"/>.
    ///
    /// Nutrition values are stored per 100g in <c>NutritionCache</c> rows and scaled by an
    /// estimated gram weight derived from the ingredient's quantity string (e.g., "2 cups" → 480g).
    /// Cached rows are refreshed after 90 days. Calories use USDA nutrient ID 1008 (Energy kcal).
    /// Unmatched ingredients contribute zeroed nutrition rows with <c>Matched = false</c>.
    /// </summary>
    public class NutritionService : INutritionService
    {
        private readonly PlaterlyDbContext _db;
        private readonly UsdaClient _usda;
        private readonly ILogger<NutritionService> _logger;

        public NutritionService(
            PlaterlyDbContext db,
            UsdaClient usda,
            ILogger<NutritionService> logger)
        {
            _db = db;
            _usda = usda;
            _logger = logger;
        }

        /// <summary>
        /// Returns per-ingredient and total nutrition for a recipe. Loads the recipe with its
        /// ingredients, resolves nutrition data from the DB cache (or USDA if not cached), scales
        /// values by estimated gram weight, and sums into a total breakdown. Returns <c>null</c>
        /// when the recipe does not exist or is soft-deleted.
        /// </summary>
        /// <param name="recipeId">Recipe to calculate nutrition for.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Nutrition response with per-ingredient rows and total breakdown, or <c>null</c>.</returns>
        public async Task<RecipeNutritionResponse?> GetRecipeNutritionAsync(
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
            var total = new NutritionBreakdown();
            var ingredientResults = new List<IngredientNutritionDto>();
            int matched = 0;

            foreach (var ingredient in ingredients)
            {
                var ingredientName = ingredient.GlobalIngredient?.Name ?? "Unknown";
                var (estimatedGrams, qty, unit) = ParseQuantity(ingredient.Quantity);

                var cache = await GetOrFetchNutritionAsync(
                    ingredient.GlobalIngredientId, ingredientName, ct);

                if (cache != null)
                {
                    matched++;
                    var factor = estimatedGrams / 100m;
                    var nutrition = new NutritionBreakdown
                    {
                        Calories = Math.Round(cache.CaloriesPer100g * factor, 1),
                        ProteinG = Math.Round(cache.ProteinPer100g * factor, 1),
                        CarbsG = Math.Round(cache.CarbsPer100g * factor, 1),
                        FatG = Math.Round(cache.FatPer100g * factor, 1),
                        FiberG = Math.Round(cache.FiberPer100g * factor, 1),
                        SodiumMg = Math.Round(cache.SodiumMgPer100g * factor, 1),
                        SugarG = Math.Round(cache.SugarPer100g * factor, 1),
                        SaturatedFatG = Math.Round(cache.SaturatedFatPer100g * factor, 1),
                        CholesterolMg = Math.Round(cache.CholesterolMgPer100g * factor, 1)
                    };

                    AddTo(total, nutrition);

                    ingredientResults.Add(new IngredientNutritionDto
                    {
                        IngredientName = ingredientName,
                        Quantity = qty,
                        Unit = unit,
                        EstimatedGrams = estimatedGrams,
                        Nutrition = nutrition,
                        MatchConfidence = cache.MatchConfidence,
                        Matched = true
                    });
                }
                else
                {
                    ingredientResults.Add(new IngredientNutritionDto
                    {
                        IngredientName = ingredientName,
                        Quantity = qty,
                        Unit = unit,
                        EstimatedGrams = estimatedGrams,
                        Nutrition = new NutritionBreakdown(),
                        MatchConfidence = 0,
                        Matched = false
                    });
                }
            }

            // Default to 1 serving if not specified
            var servings = Math.Max(1, 1);
            var perServing = DivideBreakdown(total, servings);

            return new RecipeNutritionResponse
            {
                RecipeId = recipeId,
                RecipeTitle = recipe.Title,
                Servings = servings,
                PerServing = perServing,
                Total = total,
                Ingredients = ingredientResults,
                MatchedIngredients = matched,
                TotalIngredients = ingredients.Count
            };
        }

        /// <summary>
        /// Estimates nutrition for an ad-hoc list of ingredient name/quantity pairs without a
        /// stored recipe. Looks up each ingredient in <c>GlobalIngredients</c> by case-insensitive
        /// name match first; falls back to a direct USDA text search for unrecognised names.
        /// Results are scaled by estimated grams and divided by <c>request.Servings</c>.
        /// </summary>
        /// <param name="request">Ingredient list with names, quantities, units, and serving count.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Nutrition estimate with per-ingredient rows and per-serving totals.</returns>
        public async Task<NutritionEstimateResponse> EstimateNutritionAsync(
            NutritionEstimateRequest request,
            CancellationToken ct = default)
        {
            var total = new NutritionBreakdown();
            var ingredientResults = new List<IngredientNutritionDto>();
            int matched = 0;

            foreach (var input in request.Ingredients)
            {
                var quantityStr = input.Unit != null
                    ? $"{input.Quantity} {input.Unit}"
                    : input.Quantity;
                var (estimatedGrams, qty, unit) = ParseQuantity(quantityStr);

                // Try to find in our global ingredients first
                var globalIngredient = await _db.GlobalIngredients
                    .Where(g => g.Name.ToLower() == input.Name.ToLower())
                    .Select(g => new { g.Id, g.Name })
                    .FirstOrDefaultAsync(ct);

                NutritionCache? cache = null;
                if (globalIngredient != null)
                {
                    cache = await GetOrFetchNutritionAsync(
                        globalIngredient.Id, globalIngredient.Name, ct);
                }
                else
                {
                    // Direct USDA lookup by name
                    cache = await FetchFromUsdaAsync(Guid.Empty, input.Name, ct);
                }

                if (cache != null)
                {
                    matched++;
                    var factor = estimatedGrams / 100m;
                    var nutrition = new NutritionBreakdown
                    {
                        Calories = Math.Round(cache.CaloriesPer100g * factor, 1),
                        ProteinG = Math.Round(cache.ProteinPer100g * factor, 1),
                        CarbsG = Math.Round(cache.CarbsPer100g * factor, 1),
                        FatG = Math.Round(cache.FatPer100g * factor, 1),
                        FiberG = Math.Round(cache.FiberPer100g * factor, 1),
                        SodiumMg = Math.Round(cache.SodiumMgPer100g * factor, 1),
                        SugarG = Math.Round(cache.SugarPer100g * factor, 1),
                        SaturatedFatG = Math.Round(cache.SaturatedFatPer100g * factor, 1),
                        CholesterolMg = Math.Round(cache.CholesterolMgPer100g * factor, 1)
                    };

                    AddTo(total, nutrition);

                    ingredientResults.Add(new IngredientNutritionDto
                    {
                        IngredientName = input.Name,
                        Quantity = qty ?? input.Quantity,
                        Unit = unit ?? input.Unit,
                        EstimatedGrams = estimatedGrams,
                        Nutrition = nutrition,
                        MatchConfidence = cache.MatchConfidence,
                        Matched = true
                    });
                }
                else
                {
                    ingredientResults.Add(new IngredientNutritionDto
                    {
                        IngredientName = input.Name,
                        Quantity = input.Quantity,
                        Unit = input.Unit,
                        EstimatedGrams = estimatedGrams,
                        Nutrition = new NutritionBreakdown(),
                        MatchConfidence = 0,
                        Matched = false
                    });
                }
            }

            var servings = Math.Max(1, request.Servings);
            var perServing = DivideBreakdown(total, servings);

            return new NutritionEstimateResponse
            {
                Servings = servings,
                PerServing = perServing,
                Total = total,
                Ingredients = ingredientResults,
                MatchedIngredients = matched,
                TotalIngredients = request.Ingredients.Count
            };
        }

        // ========== Helpers ==========

        private async Task<NutritionCache?> GetOrFetchNutritionAsync(
            Guid globalIngredientId, string ingredientName, CancellationToken ct)
        {
            // Check cache first
            var cached = await _db.NutritionCaches
                .Where(n => n.GlobalIngredientId == globalIngredientId)
                .FirstOrDefaultAsync(ct);

            if (cached != null)
            {
                // Refresh if older than 90 days
                if ((DateTime.UtcNow - cached.FetchedAt).TotalDays < 90)
                    return cached;
            }

            return await FetchFromUsdaAsync(globalIngredientId, ingredientName, ct);
        }

        private async Task<NutritionCache?> FetchFromUsdaAsync(
            Guid globalIngredientId, string ingredientName, CancellationToken ct)
        {
            var match = await _usda.SearchFoodAsync(ingredientName, ct);
            if (match == null) return null;

            var cache = new NutritionCache
            {
                GlobalIngredientId = globalIngredientId,
                UsdaFdcId = match.FdcId,
                UsdaDescription = match.Description,
                CaloriesPer100g = match.CaloriesPer100g,
                ProteinPer100g = match.ProteinPer100g,
                CarbsPer100g = match.CarbsPer100g,
                FatPer100g = match.FatPer100g,
                FiberPer100g = match.FiberPer100g,
                SodiumMgPer100g = match.SodiumMgPer100g,
                SugarPer100g = match.SugarPer100g,
                SaturatedFatPer100g = match.SaturatedFatPer100g,
                CholesterolMgPer100g = match.CholesterolMgPer100g,
                MatchConfidence = 70, // Default for USDA text-search matches
                FetchedAt = DateTime.UtcNow
            };

            // Only persist to DB if we have a real global ingredient ID
            if (globalIngredientId != Guid.Empty)
            {
                var existing = await _db.NutritionCaches
                    .Where(n => n.GlobalIngredientId == globalIngredientId)
                    .FirstOrDefaultAsync(ct);

                if (existing != null)
                {
                    existing.UsdaFdcId = cache.UsdaFdcId;
                    existing.UsdaDescription = cache.UsdaDescription;
                    existing.CaloriesPer100g = cache.CaloriesPer100g;
                    existing.ProteinPer100g = cache.ProteinPer100g;
                    existing.CarbsPer100g = cache.CarbsPer100g;
                    existing.FatPer100g = cache.FatPer100g;
                    existing.FiberPer100g = cache.FiberPer100g;
                    existing.SodiumMgPer100g = cache.SodiumMgPer100g;
                    existing.SugarPer100g = cache.SugarPer100g;
                    existing.SaturatedFatPer100g = cache.SaturatedFatPer100g;
                    existing.CholesterolMgPer100g = cache.CholesterolMgPer100g;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _db.NutritionCaches.Add(cache);
                }

                await _db.SaveChangesAsync(ct);
            }

            return cache;
        }

        /// <summary>
        /// Simple quantity parser. Returns estimated grams, display quantity, and unit.
        /// Handles common formats: "2 cups", "1/2 tsp", "200g", "3 pieces"
        /// </summary>
        private static (decimal estimatedGrams, string? qty, string? unit) ParseQuantity(string? quantity)
        {
            if (string.IsNullOrWhiteSpace(quantity))
                return (100m, null, null); // Default 100g if no quantity

            var parts = quantity.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            decimal amount = 1m;
            string? unitStr = null;

            if (parts.Length >= 1)
            {
                if (decimal.TryParse(parts[0], out var parsed))
                {
                    amount = parsed;
                }
                else if (parts[0].Contains('/'))
                {
                    var frac = parts[0].Split('/');
                    if (frac.Length == 2 && decimal.TryParse(frac[0], out var num) &&
                        decimal.TryParse(frac[1], out var den) && den != 0)
                    {
                        amount = num / den;
                    }
                }
            }

            if (parts.Length >= 2)
                unitStr = parts[1].ToLower().Trim();

            // Approximate gram conversions for common cooking units
            var grams = unitStr switch
            {
                "g" or "gram" or "grams" => amount,
                "kg" or "kilogram" or "kilograms" => amount * 1000m,
                "oz" or "ounce" or "ounces" => amount * 28.35m,
                "lb" or "lbs" or "pound" or "pounds" => amount * 453.6m,
                "cup" or "cups" => amount * 240m,
                "tbsp" or "tablespoon" or "tablespoons" => amount * 15m,
                "tsp" or "teaspoon" or "teaspoons" => amount * 5m,
                "ml" or "milliliter" or "milliliters" => amount, // ~1g per ml for most ingredients
                "l" or "liter" or "liters" or "litre" or "litres" => amount * 1000m,
                "piece" or "pieces" or "whole" => amount * 100m, // rough estimate
                "clove" or "cloves" => amount * 5m,
                "pinch" => amount * 0.5m,
                "dash" => amount * 1m,
                "bunch" => amount * 50m,
                "slice" or "slices" => amount * 30m,
                "can" or "cans" => amount * 400m,
                _ => amount * 100m // fallback: assume ~100g per unit
            };

            return (Math.Max(1m, grams), parts.Length >= 1 ? parts[0] : null, unitStr);
        }

        private static void AddTo(NutritionBreakdown total, NutritionBreakdown add)
        {
            total.Calories += add.Calories;
            total.ProteinG += add.ProteinG;
            total.CarbsG += add.CarbsG;
            total.FatG += add.FatG;
            total.FiberG += add.FiberG;
            total.SodiumMg += add.SodiumMg;
            total.SugarG += add.SugarG;
            total.SaturatedFatG += add.SaturatedFatG;
            total.CholesterolMg += add.CholesterolMg;
        }

        private static NutritionBreakdown DivideBreakdown(NutritionBreakdown total, int servings)
        {
            if (servings <= 1) return total;
            return new NutritionBreakdown
            {
                Calories = Math.Round(total.Calories / servings, 1),
                ProteinG = Math.Round(total.ProteinG / servings, 1),
                CarbsG = Math.Round(total.CarbsG / servings, 1),
                FatG = Math.Round(total.FatG / servings, 1),
                FiberG = Math.Round(total.FiberG / servings, 1),
                SodiumMg = Math.Round(total.SodiumMg / servings, 1),
                SugarG = Math.Round(total.SugarG / servings, 1),
                SaturatedFatG = Math.Round(total.SaturatedFatG / servings, 1),
                CholesterolMg = Math.Round(total.CholesterolMg / servings, 1)
            };
        }
    }
}
