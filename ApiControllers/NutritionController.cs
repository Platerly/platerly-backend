using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Nutrition;

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
    public class NutritionController : ControllerBase
    {
        private readonly INutritionService _nutrition;
        private readonly ILogger<NutritionController> _logger;

        public NutritionController(
            INutritionService nutrition,
            ILogger<NutritionController> logger)
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
        public async Task<ActionResult<RecipeNutritionResponse>> GetRecipeNutrition(
            Guid recipeId,
            CancellationToken ct = default)
        {
            var result = await _nutrition.GetRecipeNutritionAsync(recipeId, ct);
            if (result == null) return NotFound("Recipe not found");
            return Ok(result);
        }

        /// <summary>
        /// Estimate nutrition from a list of ingredients.
        /// Send ingredient names with optional quantities and units.
        /// </summary>
        [HttpPost("estimate")]
        public async Task<ActionResult<NutritionEstimateResponse>> EstimateNutrition(
            [FromBody] NutritionEstimateRequest request,
            CancellationToken ct = default)
        {
            if (request.Ingredients == null || request.Ingredients.Count == 0)
            {
                return BadRequest("At least one ingredient is required");
            }

            if (request.Ingredients.Count > 100)
            {
                return BadRequest("Maximum 100 ingredients per request");
            }

            request.Servings = Math.Clamp(request.Servings, 1, 100);

            var result = await _nutrition.EstimateNutritionAsync(request, ct);
            return Ok(result);
        }
    }
}
