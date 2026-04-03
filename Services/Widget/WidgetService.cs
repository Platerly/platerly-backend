using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Widget
{
    /// <summary>
    /// B2B data layer — manages embeddable recipe-widget configurations and serves the widget feed.
    /// Depends on <see cref="PlaterlyDbContext"/> and <see cref="IConfiguration"/> for the frontend
    /// base URL used to generate the embed script tag.
    ///
    /// B2B API key holders configure a widget (theme JSON, max recipes, dietary/region filters, logo)
    /// via <see cref="ConfigureWidgetAsync"/>; Recipzee returns an HTML <c>&lt;script&gt;</c> tag
    /// embedding the widget on their domain. The public widget feed is served without authentication
    /// by <see cref="GetWidgetFeedAsync"/> using the widget config ID.
    /// </summary>
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

        /// <summary>
        /// Creates or updates the widget configuration for the given API key.
        /// <c>MaxRecipes</c> is clamped to 1–50. Returns a <see cref="WidgetConfigResponse"/>
        /// including a ready-to-use HTML <c>&lt;script&gt;</c> embed tag.
        /// </summary>
        /// <param name="apiKeyId">The authenticated B2B API key's ID.</param>
        /// <param name="request">Widget configuration settings.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The saved widget configuration including embed URL and script tag.</returns>
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

            var frontendBaseUrl = _config["FrontendBaseUrl"] ?? "https://www.recipzee.com";
            var embedJsUrl = $"{frontendBaseUrl}/widget/v1/embed.js";
            var scriptTag = $"<script src=\"{embedJsUrl}\" data-config-id=\"{existing.Id}\" async></script>";

            return new WidgetConfigResponse
            {
                Id = existing.Id,
                ThemeJson = existing.ThemeJson,
                MaxRecipes = existing.MaxRecipes,
                DietaryFilters = existing.DietaryFilters,
                RegionFilter = existing.RegionFilter,
                LogoUrl = existing.LogoUrl,
                IsActive = existing.IsActive,
                EmbedUrl = embedJsUrl,
                ScriptTag = scriptTag
            };
        }

        /// <summary>
        /// Returns a recipe feed filtered by the widget's region and dietary filter settings.
        /// Called publicly (no auth) by the embedded widget JavaScript. Returns an empty feed
        /// (not an error) when the widget config is not found or is inactive.
        /// </summary>
        /// <param name="widgetConfigId">The widget config ID embedded in the script tag.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A <see cref="WidgetFeedResponse"/> with up to <c>MaxRecipes</c> recipes and total count.</returns>
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

        /// <summary>
        /// Returns the public-facing branding configuration for a widget (accent colour, background,
        /// text colour, border, muted colour, placeholder text). Parses the stored JSON theme blob
        /// and deserialises only safe, non-sensitive fields for the frontend embed script.
        /// Returns <c>null</c> when the config is not found or inactive.
        /// </summary>
        /// <param name="configId">The widget configuration ID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A <see cref="WidgetPublicConfigDto"/> with branding fields, or <c>null</c>.</returns>
        public async Task<WidgetPublicConfigDto?> GetPublicConfigAsync(
            Guid configId,
            CancellationToken ct = default)
        {
            var config = await _db.WidgetConfigs
                .Where(w => w.Id == configId && w.IsActive)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);

            if (config == null) return null;

            var dto = new WidgetPublicConfigDto
            {
                LogoUrl = config.LogoUrl
            };

            // Parse ThemeJson for branding overrides
            if (!string.IsNullOrWhiteSpace(config.ThemeJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(config.ThemeJson);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("accentColor", out var v) && v.ValueKind == JsonValueKind.String)
                        dto.AccentColor = v.GetString()!;
                    if (root.TryGetProperty("backgroundColor", out v) && v.ValueKind == JsonValueKind.String)
                        dto.BackgroundColor = v.GetString()!;
                    if (root.TryGetProperty("textColor", out v) && v.ValueKind == JsonValueKind.String)
                        dto.TextColor = v.GetString()!;
                    if (root.TryGetProperty("borderColor", out v) && v.ValueKind == JsonValueKind.String)
                        dto.BorderColor = v.GetString()!;
                    if (root.TryGetProperty("mutedColor", out v) && v.ValueKind == JsonValueKind.String)
                        dto.MutedColor = v.GetString()!;
                    if (root.TryGetProperty("placeholderText", out v) && v.ValueKind == JsonValueKind.String)
                        dto.PlaceholderText = v.GetString()!;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse ThemeJson for widget config {ConfigId}", configId);
                }
            }

            return dto;
        }
    }
}
