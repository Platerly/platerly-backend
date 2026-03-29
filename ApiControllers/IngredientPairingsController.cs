using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Ingredient Pairings API.
    /// Exposes ingredient co-occurrence and affinity data from IngredientPairing.
    /// Filterable by seed ingredient, affinity threshold, region, and cuisine.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/b2b/ingredients")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class IngredientPairingsController : B2BControllerBase
    {
        private readonly IIngredientPairingsService _pairings;
        private readonly ILogger<IngredientPairingsController> _logger;

        private static readonly HashSet<string> ValidSortBy =
            new(StringComparer.OrdinalIgnoreCase) { "affinity", "cooccurrence" };

        public IngredientPairingsController(
            IIngredientPairingsService pairings,
            ISafetyMetaService safetyMeta,
            ILogger<IngredientPairingsController> logger)
            : base(safetyMeta)
        {
            _pairings = pairings;
            _logger = logger;
        }

        /// <summary>
        /// Get ingredient pairings sorted by affinity score.
        ///
        /// Pass an ingredientId to get pairings for a specific ingredient.
        /// Omit it to browse global pairings across the platform.
        ///
        /// The affinity score (0–100) reflects how often the two ingredients
        /// appear together relative to how often they appear separately.
        /// Higher = stronger culinary association.
        /// </summary>
        /// <param name="ingredientId">Optional seed ingredient ID. Returns all pairings involving this ingredient.</param>
        /// <param name="minAffinity">Minimum affinity score 0–100 (default 0).</param>
        /// <param name="minCoOccurrence">Minimum co-occurrence count (default 0).</param>
        /// <param name="region">Optional region filter (e.g., "Mediterranean").</param>
        /// <param name="cuisine">Optional cuisine type filter (e.g., "Italian").</param>
        /// <param name="take">Number of results (default 50, max 200).</param>
        /// <param name="sortBy">Sort: "affinity" (default) or "cooccurrence".</param>
        [HttpGet("pairings")]
        public async Task<ActionResult> GetPairings(
            [FromQuery] Guid? ingredientId = null,
            [FromQuery] decimal minAffinity = 0,
            [FromQuery] int minCoOccurrence = 0,
            [FromQuery] string? region = null,
            [FromQuery] string? cuisine = null,
            [FromQuery] int take = 50,
            [FromQuery] string sortBy = "affinity",
            CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(sortBy) && !ValidSortBy.Contains(sortBy))
                return ApiError($"Invalid sortBy value '{sortBy}'. Use: affinity, cooccurrence.");

            minAffinity = Math.Clamp(minAffinity, 0, 100);
            minCoOccurrence = Math.Max(minCoOccurrence, 0);
            take = Math.Clamp(take, 1, 200);

            var request = new IngredientPairingsRequest
            {
                IngredientId = ingredientId,
                MinAffinityScore = minAffinity,
                MinCoOccurrenceCount = minCoOccurrence,
                Region = region,
                Cuisine = cuisine,
                Take = take,
                SortBy = sortBy
            };

            var result = await _pairings.GetPairingsAsync(request, ct);

            // 404 when a specific ingredient was requested but not found
            if (ingredientId.HasValue && result.SeedIngredient == null)
                return ApiError($"Ingredient '{ingredientId}' not found.", 404);

            return SafetyOk(result);
        }
    }
}
