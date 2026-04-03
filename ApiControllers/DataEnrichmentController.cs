using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Data Enrichment API — layer: B2B, base route: <c>api/DataEnrichment</c>.
    /// Enriches raw ingredient names and recipe payloads with structured metadata from the
    /// Platerly/Recipzee knowledge base: allergen classifications, dietary tags, ingredient
    /// categories, top substitutes, cuisine classification, and estimated difficulty.
    /// All responses are wrapped in the Phase 2 <see cref="SafetyApiResponse{T}"/> envelope.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
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
        /// <c>POST api/DataEnrichment/ingredients</c> — Batch-enrich ingredient names with metadata.
        /// For each ingredient name supplied, the service fuzzy-matches it against the global
        /// ingredient database and returns allergen types, nutritional category, dietary flags,
        /// and the top substitutes with confidence scores.
        /// </summary>
        /// <param name="request">
        /// Request body containing <c>IngredientNames</c> — a list of 1–100 free-text ingredient names.
        /// </param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping per-ingredient enrichment results.<br/>
        /// <c>400 Bad Request</c> — list is null/empty, or contains more than 100 names.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
        /// <c>POST api/DataEnrichment/recipe</c> — Enrich a recipe with computed metadata.
        /// Accepts a recipe title, an ingredient list (up to 200 items), and an optional region.
        /// Returns inferred dietary tags (vegan, halal, keto, etc.), aggregated allergen warnings,
        /// an estimated difficulty level, and a cuisine classification.
        /// </summary>
        /// <param name="request">
        /// Request body with <c>Title</c> (optional), <c>Ingredients</c> (1–200 items), and optional <c>Region</c>.
        /// </param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the enriched recipe metadata.<br/>
        /// <c>400 Bad Request</c> — ingredient list is null/empty, or exceeds 200 items.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
