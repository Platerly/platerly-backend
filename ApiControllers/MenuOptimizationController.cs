using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Menu Optimization API.
    /// Analyze menu composition and get recipe suggestions based on constraints.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class MenuOptimizationController : B2BControllerBase
    {
        private readonly IMenuOptimizationService _service;
        private readonly ILogger<MenuOptimizationController> _logger;

        public MenuOptimizationController(
            IMenuOptimizationService service,
            ILogger<MenuOptimizationController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// Analyze a menu of recipes.
        /// Submit recipe IDs and get diversity score, allergen gaps, dietary balance, and recommendations.
        /// </summary>
        [HttpPost("analyze")]
        public async Task<ActionResult> AnalyzeMenu(
            [FromBody] MenuAnalysisRequest request,
            CancellationToken ct = default)
        {
            if (request.RecipeIds == null || request.RecipeIds.Count == 0)
                return ApiError("At least one recipe ID is required.");

            if (request.RecipeIds.Count > 200)
                return ApiError("Maximum 200 recipes per analysis.");

            var result = await _service.AnalyzeMenuAsync(request, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// Get recipe suggestions based on constraints.
        /// Filter by dietary tags, region, and excluded allergens.
        /// </summary>
        /// <param name="region">Optional region filter</param>
        /// <param name="dietaryTags">Optional comma-separated dietary tags</param>
        /// <param name="excludeAllergens">Optional comma-separated allergens to exclude</param>
        /// <param name="take">Number of suggestions (default 10, max 50)</param>
        [HttpGet("suggestions")]
        public async Task<ActionResult> GetSuggestions(
            [FromQuery] string? region = null,
            [FromQuery] string? dietaryTags = null,
            [FromQuery] string? excludeAllergens = null,
            [FromQuery] int take = 10,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 50);

            var request = new MenuSuggestionsRequest
            {
                Region = region,
                DietaryTags = dietaryTags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                ExcludeAllergens = excludeAllergens?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                Take = take
            };

            var result = await _service.GetSuggestionsAsync(request, ct);
            return SafetyOk(result);
        }
    }
}
