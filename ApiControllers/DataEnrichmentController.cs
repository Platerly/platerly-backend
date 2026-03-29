using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Data Enrichment API.
    /// Enrich ingredient and recipe data with allergens, dietary tags, substitutes, and metadata.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class DataEnrichmentController : B2BControllerBase
    {
        private readonly IDataEnrichmentService _enrichment;
        private readonly ILogger<DataEnrichmentController> _logger;

        public DataEnrichmentController(
            IDataEnrichmentService enrichment,
            ILogger<DataEnrichmentController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _enrichment = enrichment;
            _logger = logger;
        }

        /// <summary>
        /// Enrich ingredient names with allergens, categories, dietary flags, and top substitutes.
        /// Send up to 100 ingredient names and get back enriched data for each.
        /// </summary>
        [HttpPost("ingredients")]
        public async Task<ActionResult> EnrichIngredients(
            [FromBody] IngredientEnrichmentRequest request,
            CancellationToken ct = default)
        {
            if (request.IngredientNames == null || request.IngredientNames.Count == 0)
                return ApiError("At least one ingredient name is required.");

            if (request.IngredientNames.Count > 100)
                return ApiError("Maximum 100 ingredients per request.");

            var result = await _enrichment.EnrichIngredientsAsync(request, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// Enrich a recipe with dietary tags, allergen warnings, difficulty estimate, and cuisine classification.
        /// Send a recipe title, ingredient list, and optional region.
        /// </summary>
        [HttpPost("recipe")]
        public async Task<ActionResult> EnrichRecipe(
            [FromBody] RecipeEnrichmentRequest request,
            CancellationToken ct = default)
        {
            if (request.Ingredients == null || request.Ingredients.Count == 0)
                return ApiError("At least one ingredient is required.");

            if (request.Ingredients.Count > 200)
                return ApiError("Maximum 200 ingredients per request.");

            var result = await _enrichment.EnrichRecipeAsync(request, ct);
            return SafetyOk(result);
        }
    }
}
