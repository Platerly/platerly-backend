using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Widget
{
    public class WidgetService : IWidgetService
    {
        private readonly PlaterlyDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<WidgetService> _logger;

        public WidgetService(
            PlaterlyDbContext db,
            IConfiguration config,
            ILogger<WidgetService> logger)
        {
            _db = db;
            _config = config;
            _logger = logger;
        }

        public async Task<WidgetConfigResponse> ConfigureWidgetAsync(
            Guid apiKeyId, WidgetConfigRequest request,
            CancellationToken ct = default)
        {
            var existing = await _db.WidgetConfigs
                .Where(w => w.ApiKeyId == apiKeyId)
                .FirstOrDefaultAsync(ct);

            if (existing != null)
            {
                existing.ThemeJson = request.ThemeJson;
                existing.MaxRecipes = Math.Clamp(request.MaxRecipes, 1, 50);
                existing.DietaryFilters = request.DietaryFilters;
                existing.RegionFilter = request.RegionFilter;
                existing.LogoUrl = request.LogoUrl;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                existing = new WidgetConfig
                {
                    ApiKeyId = apiKeyId,
                    ThemeJson = request.ThemeJson,
                    MaxRecipes = Math.Clamp(request.MaxRecipes, 1, 50),
                    DietaryFilters = request.DietaryFilters,
                    RegionFilter = request.RegionFilter,
                    LogoUrl = request.LogoUrl,
                    IsActive = true
                };
                _db.WidgetConfigs.Add(existing);
            }

            await _db.SaveChangesAsync(ct);

            var baseUrl = _config["BackendBaseUrl"] ?? "https://recipzee.com";
            return new WidgetConfigResponse
            {
                Id = existing.Id,
                ThemeJson = existing.ThemeJson,
                MaxRecipes = existing.MaxRecipes,
                DietaryFilters = existing.DietaryFilters,
                RegionFilter = existing.RegionFilter,
                LogoUrl = existing.LogoUrl,
                IsActive = existing.IsActive,
                EmbedUrl = $"{baseUrl}/api/Widget/feed?configId={existing.Id}"
            };
        }

        public async Task<WidgetFeedResponse> GetWidgetFeedAsync(
            Guid widgetConfigId,
            CancellationToken ct = default)
        {
            var config = await _db.WidgetConfigs
                .Where(w => w.Id == widgetConfigId && w.IsActive)
                .FirstOrDefaultAsync(ct);

            if (config == null)
            {
                return new WidgetFeedResponse { Recipes = new(), TotalAvailable = 0 };
            }

            var query = _db.Recipes
                .Include(r => r.User)
                .Include(r => r.RecipeTags)
                    .ThenInclude(rt => rt.Tag)
                .Where(r => !r.IsDeleted);

            // Apply region filter
            if (!string.IsNullOrEmpty(config.RegionFilter))
            {
                query = query.Where(r => r.Region == config.RegionFilter);
            }

            // Apply dietary filters
            if (!string.IsNullOrEmpty(config.DietaryFilters))
            {
                var filters = config.DietaryFilters
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(f => f.ToLower())
                    .ToList();

                if (filters.Count > 0)
                {
                    query = query.Where(r => r.RecipeTags.Any(rt =>
                        rt.Tag != null && filters.Contains(rt.Tag.Name.ToLower())));
                }
            }

            var totalAvailable = await query.CountAsync(ct);

            var recipes = await query
                .OrderByDescending(r => r.CreatedAt)
                .Take(config.MaxRecipes)
                .Select(r => new WidgetFeedRecipeDto
                {
                    Id = r.Id,
                    Title = r.Title,
                    Description = r.Description.Length > 200
                        ? r.Description.Substring(0, 200) + "..."
                        : r.Description,
                    ImageUrl = r.ImageUrl,
                    Region = r.Region,
                    PrepTimeMinutes = r.PrepTimeMinutes,
                    CookTimeMinutes = r.CookTimeMinutes,
                    Difficulty = r.Difficulty.HasValue ? r.Difficulty.Value.ToString() : null,
                    Tags = r.RecipeTags
                        .Where(rt => rt.Tag != null)
                        .Select(rt => rt.Tag!.Name)
                        .ToList(),
                    AuthorUsername = r.User != null ? r.User.Username : ""
                })
                .ToListAsync(ct);

            return new WidgetFeedResponse
            {
                Recipes = recipes,
                TotalAvailable = totalAvailable
            };
        }
    }
}
