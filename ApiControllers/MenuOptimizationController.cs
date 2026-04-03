using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Menu Optimization API — layer: B2B, base route: <c>api/MenuOptimization</c>.
    /// Helps food-service and hospitality partners evaluate and improve their menus.
    /// Provides diversity scoring, allergen gap analysis, dietary balance reporting,
    /// and constraint-based recipe suggestions sourced from the Recipzee recipe index.
    /// All responses are wrapped in the Phase 2 <see cref="SafetyApiResponse{T}"/> envelope.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
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
        /// <c>POST api/MenuOptimization/analyze</c> — Analyse the composition of a menu.
        /// Submit a list of Recipzee recipe IDs (1–200) and receive a detailed report covering:
        /// overall diversity score, allergen coverage gaps, dietary tag balance (vegan / gluten-free /
        /// halal / etc.), cuisine distribution, and actionable improvement recommendations.
        /// </summary>
        /// <param name="request">
        /// Request body with <c>RecipeIds</c> — a list of 1–200 Recipzee recipe GUIDs to analyse.
        /// </param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the menu analysis report.<br/>
        /// <c>400 Bad Request</c> — recipe ID list is null/empty, or exceeds 200 items.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
        /// <c>GET api/MenuOptimization/suggestions</c> — Get constraint-filtered recipe suggestions.
        /// Returns recipes from the Recipzee index that match the supplied dietary, regional,
        /// and allergen-exclusion constraints. Suitable for gap-filling a partially built menu.
        /// </summary>
        /// <param name="region">Optional region/cuisine filter (e.g., <c>"Mediterranean"</c>).</param>
        /// <param name="dietaryTags">
        /// Optional comma-separated dietary tags to require (e.g., <c>"vegan,gluten-free"</c>).
        /// Values are split on commas with whitespace trimming.
        /// </param>
        /// <param name="excludeAllergens">
        /// Optional comma-separated allergen types to exclude (e.g., <c>"dairy,nuts"</c>).
        /// Values are split on commas with whitespace trimming.
        /// </param>
        /// <param name="take">Number of suggestions to return. Clamped to 1–50; default <c>10</c>.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the suggestion list.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
                // Split comma-separated strings into lists, trimming whitespace from each token
                DietaryTags = dietaryTags?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                ExcludeAllergens = excludeAllergens?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                Take = take
            };

            var result = await _service.GetSuggestionsAsync(request, ct);
            return SafetyOk(result);
        }
    }
}
