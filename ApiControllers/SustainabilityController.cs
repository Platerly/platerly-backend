using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Sustainability Scoring API.
    /// Environmental impact scoring for recipes based on ingredient sustainability data.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class SustainabilityController : B2BControllerBase
    {
        private readonly ISustainabilityService _service;
        private readonly ILogger<SustainabilityController> _logger;

        public SustainabilityController(
            ISustainabilityService service,
            ILogger<SustainabilityController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// Get sustainability score for a recipe.
        /// Includes carbon footprint, water usage, seasonality, and per-ingredient breakdown.
        /// Score 0-100 with A-E rating.
        /// </summary>
        /// <param name="recipeId">The recipe ID to score</param>
        [HttpGet("recipe/{recipeId}")]
        public async Task<ActionResult> GetRecipeSustainability(
            Guid recipeId,
            CancellationToken ct = default)
        {
            var result = await _service.GetRecipeSustainabilityAsync(recipeId, ct);
            if (result == null) return ApiError("Recipe not found.", 404);
            return SafetyOk(result);
        }

        /// <summary>
        /// Compare sustainability scores across multiple recipes.
        /// </summary>
        [HttpPost("compare")]
        public async Task<ActionResult> CompareRecipes(
            [FromBody] SustainabilityCompareRequest request,
            CancellationToken ct = default)
        {
            if (request.RecipeIds == null || request.RecipeIds.Count == 0)
                return ApiError("At least one recipe ID is required.");

            if (request.RecipeIds.Count > 50)
                return ApiError("Maximum 50 recipes per comparison.");

            var result = await _service.CompareRecipesAsync(request, ct);
            return SafetyOk(result);
        }
    }
}
