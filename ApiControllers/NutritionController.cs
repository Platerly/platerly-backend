using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Nutrition;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Nutrition Estimation API — layer: B2B, base route: <c>api/Nutrition</c>.
    /// Estimates macro- and micro-nutrient profiles for stored Recipzee recipes or ad-hoc
    /// ingredient lists, using USDA FoodData Central as the underlying data source.
    /// Returns per-serving and total figures together with an ingredient match rate,
    /// enabling callers to gauge estimate reliability.
    /// All responses are wrapped in the Phase 2 <see cref="SafetyApiResponse{T}"/> envelope.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
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
        /// <c>GET api/Nutrition/recipe/{recipeId}</c> — Get estimated nutrition for a stored recipe.
        /// Resolves the recipe's ingredient list internally, fuzzy-matches each ingredient against
        /// USDA FoodData Central, and returns aggregated per-serving and total nutrition figures
        /// along with a match rate indicating how many ingredients were successfully mapped.
        /// </summary>
        /// <param name="recipeId">GUID of the Recipzee recipe to analyse.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the nutrition response.<br/>
        /// <c>404 Not Found</c> — no recipe with the given ID exists.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
        /// <c>POST api/Nutrition/estimate</c> — Estimate nutrition from an ad-hoc ingredient list.
        /// Each ingredient entry may include a free-text name and optional quantity with unit.
        /// The service fuzzy-matches names against USDA FoodData Central and computes
        /// per-serving and total macros/micros based on the supplied <c>Servings</c> value.
        /// </summary>
        /// <param name="request">
        /// Request body with <c>Ingredients</c> (1–100 items, each with <c>Name</c> and optional
        /// <c>Quantity</c>/<c>Unit</c>) and <c>Servings</c> (clamped to 1–100).
        /// </param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the nutrition estimate.<br/>
        /// <c>400 Bad Request</c> — ingredient list is null/empty, or exceeds 100 items.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
