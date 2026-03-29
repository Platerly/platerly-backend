using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Nutrition;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Nutrition Estimation API.
    /// Estimates recipe nutrition using USDA FoodData Central data.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class NutritionController : B2BControllerBase
    {
        private readonly INutritionService _nutrition;
        private readonly ILogger<NutritionController> _logger;

        public NutritionController(
            INutritionService nutrition,
            ILogger<NutritionController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _nutrition = nutrition;
            _logger = logger;
        }

        /// <summary>
        /// Get estimated nutrition for a recipe.
        /// Matches recipe ingredients to USDA FoodData Central and calculates per-serving and total nutrition.
        /// </summary>
        /// <param name="recipeId">The recipe ID to analyze</param>
        [HttpGet("recipe/{recipeId}")]
        public async Task<ActionResult> GetRecipeNutrition(
            Guid recipeId,
            CancellationToken ct = default)
        {
            var result = await _nutrition.GetRecipeNutritionAsync(recipeId, ct);
            if (result == null) return ApiError("Recipe not found.", 404);
            return SafetyOk(result);
        }

        /// <summary>
        /// Estimate nutrition from a list of ingredients.
        /// Send ingredient names with optional quantities and units.
        /// </summary>
        [HttpPost("estimate")]
        public async Task<ActionResult> EstimateNutrition(
            [FromBody] NutritionEstimateRequest request,
            CancellationToken ct = default)
        {
            if (request.Ingredients == null || request.Ingredients.Count == 0)
                return ApiError("At least one ingredient is required.");

            if (request.Ingredients.Count > 100)
                return ApiError("Maximum 100 ingredients per request.");

            request.Servings = Math.Clamp(request.Servings, 1, 100);

            var result = await _nutrition.EstimateNutritionAsync(request, ct);
            return SafetyOk(result);
        }
    }
}
