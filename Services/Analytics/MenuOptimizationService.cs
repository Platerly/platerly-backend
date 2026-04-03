using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>
    /// B2B data layer — menu analysis and optimisation suggestions for restaurant and food-service partners.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Analyses a set of recipe IDs as a "menu", computing aggregate allergen coverage,
    /// dietary-tag distribution, region diversity, and ingredient overlap.
    /// <see cref="GetSuggestionsAsync"/> returns actionable suggestions for improving allergen coverage
    /// and dietary inclusivity based on the current menu composition.
    /// </summary>
    public class MenuOptimizationService : IMenuOptimizationService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<MenuOptimizationService> _logger;

        public MenuOptimizationService(
            PlaterlyDbContext db,
            ILogger<MenuOptimizationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Analyses a set of recipe IDs as a menu, computing allergen coverage, dietary-tag distribution,
        /// region diversity, and common/unique ingredients.
        /// </summary>
        /// <param name="request">List of recipe IDs forming the menu to analyse.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A <see cref="MenuAnalysisResponse"/> with aggregate statistics and allergen flags.</returns>
        public async Task<MenuAnalysisResponse> AnalyzeMenuAsync(
            MenuAnalysisRequest request,
            CancellationToken ct = default)
        {
            var recipes = await _db.Recipes
                .Include(r => r.Ingredients!)
                    .ThenInclude(i => i.GlobalIngredient)
                        .ThenInclude(g => g!.Category)
                .Include(r => r.RecipeTags)
                    .ThenInclude(rt => rt.Tag)
                .Where(r => request.RecipeIds.Contains(r.Id) && !r.IsDeleted)
                .ToListAsync(ct);

            // Collect all ingredients and allergens in the menu
            var allIngredientIds = new HashSet<Guid>();
            var dietaryTags = new Dictionary<string, int>();
            var regions = new Dictionary<string, int>();
            var types = new Dictionary<string, int>();

            foreach (var recipe in recipes)
            {
                // Track types
                var typeStr = recipe.Type.ToString();
                types.TryGetValue(typeStr, out var typeCount);
                types[typeStr] = typeCount + 1;

                // Track regions
                if (recipe.Region != null)
                {
                    regions.TryGetValue(recipe.Region, out var regionCount);
                    regions[recipe.Region] = regionCount + 1;
                }

                // Track ingredients
                if (recipe.Ingredients != null)
                {
                    foreach (var ing in recipe.Ingredients)
                    {
                        allIngredientIds.Add(ing.GlobalIngredientId);
                    }
                }

                // Track dietary tags
                foreach (var rt in recipe.RecipeTags)
                {
                    if (rt.Tag?.Name != null)
                    {
                        dietaryTags.TryGetValue(rt.Tag.Name, out var tagCount);
                        dietaryTags[rt.Tag.Name] = tagCount + 1;
                    }
                }
            }

            // Find allergens present in menu
            var allergensCovered = await _db.GlobalIngredientAllergens
                .Where(ga => allIngredientIds.Contains(ga.GlobalIngredientId))
                .Select(ga => ga.AllergenType)
                .Distinct()
                .ToListAsync(ct);

            // Common allergens that are NOT covered
            var commonAllergens = new[] { "dairy", "gluten", "nuts", "peanuts", "soy", "egg", "fish", "shellfish" };
            var allergensNotCovered = commonAllergens
                .Where(a => !allergensCovered.Any(c => c.Equals(a, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Diversity score: based on unique types, regions, and dietary coverage
            var uniqueTypes = types.Count;
            var uniqueRegions = regions.Count;
            var uniqueIngredients = allIngredientIds.Count;
            var diversityScore = Math.Min(100,
                (uniqueTypes * 15) + (uniqueRegions * 10) + (uniqueIngredients * 0.5m) + (dietaryTags.Count * 5));

            // Find popular ingredients missing from the menu
            var topPopularIds = await _db.IngredientUsageLogs
                .GroupBy(u => u.GlobalIngredientId)
                .OrderByDescending(g => g.Count())
                .Take(20)
                .Select(g => g.Key)
                .ToListAsync(ct);

            var missingPopular = await _db.GlobalIngredients
                .Where(g => topPopularIds.Contains(g.Id) && !allIngredientIds.Contains(g.Id))
                .Select(g => g.Name)
                .Take(5)
                .ToListAsync(ct);

            // Recommendations
            var recommendations = new List<string>();
            if (uniqueTypes < 3) recommendations.Add("Consider adding more recipe types for variety.");
            if (uniqueRegions < 2) recommendations.Add("Expand regional diversity in the menu.");
            if (allergensNotCovered.Count < 3) recommendations.Add("Good allergen coverage. Consider allergen-free options.");
            if (missingPopular.Count > 0) recommendations.Add($"Popular ingredients not in menu: {string.Join(", ", missingPopular)}.");

            return new MenuAnalysisResponse
            {
                TotalRecipes = recipes.Count,
                DiversityScore = Math.Round(diversityScore, 1),
                AllergensCovered = allergensCovered,
                AllergensNotCovered = allergensNotCovered,
                DietaryTagDistribution = dietaryTags,
                RegionDistribution = regions,
                TypeDistribution = types,
                MissingPopularIngredients = missingPopular,
                Recommendations = recommendations
            };
        }

        public async Task<MenuSuggestionsResponse> GetSuggestionsAsync(
            MenuSuggestionsRequest request,
            CancellationToken ct = default)
        {
            var query = _db.Recipes
                .Include(r => r.RecipeTags)
                    .ThenInclude(rt => rt.Tag)
                .Include(r => r.Likes)
                .Where(r => !r.IsDeleted);

            // Filter by region
            if (!string.IsNullOrEmpty(request.Region))
            {
                query = query.Where(r => r.Region == request.Region);
            }

            // Filter by dietary tags
            if (request.DietaryTags != null && request.DietaryTags.Count > 0)
            {
                var lowerTags = request.DietaryTags.Select(t => t.ToLower()).ToList();
                query = query.Where(r => r.RecipeTags.Any(rt =>
                    rt.Tag != null && lowerTags.Contains(rt.Tag.Name.ToLower())));
            }

            // Exclude recipes with certain allergens
            if (request.ExcludeAllergens != null && request.ExcludeAllergens.Count > 0)
            {
                var lowerAllergens = request.ExcludeAllergens.Select(a => a.ToLower()).ToList();

                // Get ingredient IDs that have excluded allergens
                var unsafeIds = await _db.GlobalIngredientAllergens
                    .Where(ga => lowerAllergens.Contains(ga.AllergenType.ToLower()))
                    .Select(ga => ga.GlobalIngredientId)
                    .Distinct()
                    .ToListAsync(ct);

                query = query.Where(r => !r.Ingredients!
                    .Any(i => unsafeIds.Contains(i.GlobalIngredientId)));
            }

            var suggestions = await query
                .OrderByDescending(r => r.Likes.Count)
                .Take(request.Take)
                .Select(r => new MenuSuggestionDto
                {
                    RecipeId = r.Id,
                    Title = r.Title,
                    Region = r.Region,
                    Type = r.Type.ToString(),
                    Difficulty = r.Difficulty.HasValue ? r.Difficulty.Value.ToString() : null,
                    DietaryTags = r.RecipeTags
                        .Where(rt => rt.Tag != null)
                        .Select(rt => rt.Tag!.Name)
                        .ToList(),
                    EngagementScore = r.Likes.Count
                })
                .ToListAsync(ct);

            return new MenuSuggestionsResponse
            {
                Suggestions = suggestions,
                AppliedRegionFilter = request.Region,
                AppliedDietaryFilters = request.DietaryTags,
                ExcludedAllergens = request.ExcludeAllergens
            };
        }
    }
}
