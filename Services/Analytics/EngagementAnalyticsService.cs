using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>
    /// B2B data layer — platform-wide engagement analytics covering views, likes, comments, reposts, and saves.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Exposes three reporting dimensions:
    ///   <see cref="GetOverviewAsync"/> — aggregate totals and per-recipe averages across the full platform.
    ///   <see cref="GetByCategoryAsync"/> — engagement broken down by recipe type/category.
    ///   <see cref="GetByRegionAsync"/> — engagement broken down by recipe cuisine region.
    /// </summary>
    public class EngagementAnalyticsService : IEngagementAnalyticsService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<EngagementAnalyticsService> _logger;

        public EngagementAnalyticsService(
            PlaterlyDbContext db,
            ILogger<EngagementAnalyticsService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<EngagementOverviewResponse> GetOverviewAsync(
            CancellationToken ct = default)
        {
            var totalRecipes = await _db.Recipes.Where(r => !r.IsDeleted).CountAsync(ct);
            var totalViews = await _db.RecipeViews.LongCountAsync(ct);
            var totalLikes = await _db.RecipeLikes.LongCountAsync(ct);
            var totalComments = await _db.Comments.LongCountAsync(ct);
            var totalReposts = await _db.RecipeReposts.LongCountAsync(ct);
            var totalSaves = await _db.CookbookRecipes.LongCountAsync(ct);

            return new EngagementOverviewResponse
            {
                TotalRecipes = totalRecipes,
                TotalViews = totalViews,
                TotalLikes = totalLikes,
                TotalComments = totalComments,
                TotalReposts = totalReposts,
                TotalSaves = totalSaves,
                AvgViewsPerRecipe = totalRecipes > 0
                    ? Math.Round((decimal)totalViews / totalRecipes, 1) : 0,
                AvgLikesPerRecipe = totalRecipes > 0
                    ? Math.Round((decimal)totalLikes / totalRecipes, 1) : 0,
                AvgCommentsPerRecipe = totalRecipes > 0
                    ? Math.Round((decimal)totalComments / totalRecipes, 1) : 0
            };
        }

        public async Task<EngagementByCategoryResponse> GetByCategoryAsync(
            CancellationToken ct = default)
        {
            // By recipe type
            var byType = await _db.Recipes
                .Where(r => !r.IsDeleted)
                .GroupBy(r => r.Type)
                .Select(g => new EngagementByCategoryDto
                {
                    Category = g.Key.ToString(),
                    RecipeCount = g.Count(),
                    TotalViews = g.SelectMany(r => r.Likes).LongCount(), // Placeholder — see below
                    TotalLikes = g.Sum(r => r.Likes.Count),
                    TotalComments = 0,
                    AvgEngagementRate = g.Count() > 0
                        ? Math.Round((decimal)g.Sum(r => r.Likes.Count) / g.Count(), 2)
                        : 0
                })
                .OrderByDescending(x => x.RecipeCount)
                .ToListAsync(ct);

            // Fix TotalViews with separate query
            foreach (var item in byType)
            {
                if (Enum.TryParse<RecipeType>(item.Category, out var recipeType))
                {
                    item.TotalViews = await _db.RecipeViews
                        .Where(rv => rv.Recipe != null && !rv.Recipe.IsDeleted && rv.Recipe.Type == recipeType)
                        .LongCountAsync(ct);
                    item.TotalComments = await _db.Comments
                        .Where(c => c.Recipe != null && !c.Recipe.IsDeleted && c.Recipe.Type == recipeType)
                        .LongCountAsync(ct);
                }
            }

            // By difficulty
            var byDifficulty = await _db.Recipes
                .Where(r => !r.IsDeleted && r.Difficulty.HasValue)
                .GroupBy(r => r.Difficulty!.Value)
                .Select(g => new EngagementByDifficultyDto
                {
                    Difficulty = g.Key.ToString(),
                    RecipeCount = g.Count(),
                    TotalViews = 0,
                    TotalLikes = g.Sum(r => r.Likes.Count),
                    AvgEngagementRate = g.Count() > 0
                        ? Math.Round((decimal)g.Sum(r => r.Likes.Count) / g.Count(), 2)
                        : 0
                })
                .OrderBy(x => x.Difficulty)
                .ToListAsync(ct);

            foreach (var item in byDifficulty)
            {
                if (Enum.TryParse<Difficulty>(item.Difficulty, out var diff))
                {
                    item.TotalViews = await _db.RecipeViews
                        .Where(rv => rv.Recipe != null && !rv.Recipe.IsDeleted && rv.Recipe.Difficulty == diff)
                        .LongCountAsync(ct);
                }
            }

            return new EngagementByCategoryResponse
            {
                ByType = byType,
                ByDifficulty = byDifficulty
            };
        }

        public async Task<EngagementByRegionResponse> GetByRegionAsync(
            int take = 50,
            CancellationToken ct = default)
        {
            var regions = await _db.Recipes
                .Where(r => !r.IsDeleted && r.Region != null)
                .GroupBy(r => r.Region!)
                .Select(g => new EngagementByRegionDto
                {
                    Region = g.Key,
                    RecipeCount = g.Count(),
                    TotalLikes = g.Sum(r => r.Likes.Count),
                    TotalReposts = g.Sum(r => r.Reposts.Count),
                    TotalViews = 0,
                    TotalComments = 0,
                    AvgEngagementRate = g.Count() > 0
                        ? Math.Round((decimal)g.Sum(r => r.Likes.Count + r.Reposts.Count) / g.Count(), 2)
                        : 0
                })
                .OrderByDescending(x => x.RecipeCount)
                .Take(take)
                .ToListAsync(ct);

            // Fill in views and comments per region
            foreach (var region in regions)
            {
                region.TotalViews = await _db.RecipeViews
                    .Where(rv => rv.Recipe != null && !rv.Recipe.IsDeleted && rv.Recipe.Region == region.Region)
                    .LongCountAsync(ct);
                region.TotalComments = await _db.Comments
                    .Where(c => c.Recipe != null && !c.Recipe.IsDeleted && c.Recipe.Region == region.Region)
                    .LongCountAsync(ct);
            }

            return new EngagementByRegionResponse
            {
                Regions = regions
            };
        }
    }
}
