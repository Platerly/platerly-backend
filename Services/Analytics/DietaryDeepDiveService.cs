using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    public class DietaryDeepDiveService : IDietaryDeepDiveService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<DietaryDeepDiveService> _logger;

        public DietaryDeepDiveService(
            PlaterlyDbContext db,
            ILogger<DietaryDeepDiveService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<DietaryTagDetailResponse> GetTagDetailAsync(
            string tag,
            CancellationToken ct = default)
        {
            var lowerTag = tag.ToLower();
            var now = DateTime.UtcNow;

            var totalRecipes = await _db.RecipeTags
                .Include(rt => rt.Tag)
                .Where(rt => rt.Tag != null && rt.Tag.Name.ToLower() == lowerTag &&
                       rt.Recipe != null && !rt.Recipe.IsDeleted)
                .CountAsync(ct);

            var recipesLast30 = await _db.RecipeTags
                .Include(rt => rt.Tag)
                .Include(rt => rt.Recipe)
                .Where(rt => rt.Tag != null && rt.Tag.Name.ToLower() == lowerTag &&
                       rt.Recipe != null && !rt.Recipe.IsDeleted &&
                       rt.Recipe.CreatedAt >= now.AddDays(-30))
                .CountAsync(ct);

            var recipesLast90 = await _db.RecipeTags
                .Include(rt => rt.Tag)
                .Include(rt => rt.Recipe)
                .Where(rt => rt.Tag != null && rt.Tag.Name.ToLower() == lowerTag &&
                       rt.Recipe != null && !rt.Recipe.IsDeleted &&
                       rt.Recipe.CreatedAt >= now.AddDays(-90))
                .CountAsync(ct);

            var recipesPrevious30 = recipesLast90 - recipesLast30;
            var growthRate = recipesPrevious30 > 0
                ? Math.Round((decimal)(recipesLast30 - recipesPrevious30) / recipesPrevious30 * 100, 2)
                : recipesLast30 > 0 ? 100m : 0m;

            // Top ingredients in recipes with this tag
            var topIngredients = await _db.RecipeTags
                .Include(rt => rt.Tag)
                .Where(rt => rt.Tag != null && rt.Tag.Name.ToLower() == lowerTag &&
                       rt.Recipe != null && !rt.Recipe.IsDeleted)
                .SelectMany(rt => rt.Recipe!.Ingredients!)
                .Where(i => i.GlobalIngredient != null)
                .GroupBy(i => i.GlobalIngredient!.Name)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => g.Key)
                .ToListAsync(ct);

            // Top regions
            var topRegions = await _db.RecipeTags
                .Include(rt => rt.Tag)
                .Include(rt => rt.Recipe)
                .Where(rt => rt.Tag != null && rt.Tag.Name.ToLower() == lowerTag &&
                       rt.Recipe != null && !rt.Recipe.IsDeleted &&
                       rt.Recipe.Region != null)
                .GroupBy(rt => rt.Recipe!.Region!)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToListAsync(ct);

            return new DietaryTagDetailResponse
            {
                Tag = new DietaryTagDetailDto
                {
                    Tag = tag,
                    TotalRecipes = totalRecipes,
                    RecipesLast30Days = recipesLast30,
                    RecipesLast90Days = recipesLast90,
                    GrowthRateLast30Days = growthRate,
                    TopIngredients = topIngredients,
                    TopRegions = topRegions
                }
            };
        }

        public async Task<DietaryCrossoverResponse> GetCrossoverAsync(
            int take = 50,
            CancellationToken ct = default)
        {
            // Get all recipe-tag associations for non-deleted recipes
            var recipeTags = await _db.RecipeTags
                .Include(rt => rt.Tag)
                .Where(rt => rt.Tag != null && rt.Recipe != null && !rt.Recipe.IsDeleted)
                .Select(rt => new { rt.RecipeId, TagName = rt.Tag!.Name })
                .ToListAsync(ct);

            // Group by recipe to find tag co-occurrences
            var recipeGroups = recipeTags
                .GroupBy(rt => rt.RecipeId)
                .Where(g => g.Count() >= 2)
                .ToList();

            // Count tag pair co-occurrences
            var pairCounts = new Dictionary<(string, string), int>();
            var tagCounts = recipeTags
                .GroupBy(rt => rt.TagName)
                .ToDictionary(g => g.Key, g => g.Select(x => x.RecipeId).Distinct().Count());

            foreach (var group in recipeGroups)
            {
                var tags = group.Select(x => x.TagName).Distinct().OrderBy(t => t).ToList();
                for (int i = 0; i < tags.Count; i++)
                {
                    for (int j = i + 1; j < tags.Count; j++)
                    {
                        var pair = (tags[i], tags[j]);
                        pairCounts.TryGetValue(pair, out var count);
                        pairCounts[pair] = count + 1;
                    }
                }
            }

            var crossovers = pairCounts
                .OrderByDescending(kv => kv.Value)
                .Take(take)
                .Select(kv => new DietaryCrossoverDto
                {
                    TagA = kv.Key.Item1,
                    TagB = kv.Key.Item2,
                    OverlapCount = kv.Value,
                    TagATotal = tagCounts.GetValueOrDefault(kv.Key.Item1, 0),
                    TagBTotal = tagCounts.GetValueOrDefault(kv.Key.Item2, 0),
                    OverlapPercentageA = tagCounts.GetValueOrDefault(kv.Key.Item1, 0) > 0
                        ? Math.Round((decimal)kv.Value / tagCounts[kv.Key.Item1] * 100, 2)
                        : 0,
                    OverlapPercentageB = tagCounts.GetValueOrDefault(kv.Key.Item2, 0) > 0
                        ? Math.Round((decimal)kv.Value / tagCounts[kv.Key.Item2] * 100, 2)
                        : 0
                })
                .ToList();

            return new DietaryCrossoverResponse
            {
                Crossovers = crossovers,
                TotalTagsAnalyzed = tagCounts.Count
            };
        }
    }
}
