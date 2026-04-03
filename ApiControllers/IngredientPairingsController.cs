using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Ingredient Pairings API — layer: B2B, base route: <c>api/b2b/ingredients</c>.
    /// Exposes pre-computed ingredient co-occurrence and affinity data derived from the
    /// <c>IngredientPairing</c> table, which is populated from recipe ingredient analysis.
    /// Useful for building ingredient recommendation engines, smart recipe builders, and
    /// flavour-pairing tools.
    /// All responses are wrapped in the Phase 2 <see cref="SafetyApiResponse{T}"/> envelope.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
    /// </summary>
    [ApiController]
    [Route("api/b2b/ingredients")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class IngredientPairingsController : B2BControllerBase
    {
        private readonly IIngredientPairingsService _pairings;
        private readonly ILogger<IngredientPairingsController> _logger;

        // Accepted sort-by values; validated before passing to the service layer.
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
        /// <c>GET api/b2b/ingredients/pairings</c> — Get ingredient pairings ordered by affinity or co-occurrence.
        /// The affinity score (0–100) is a normalised measure of how often two ingredients appear
        /// together in recipes relative to how often they appear separately (similar to a
        /// pointwise mutual information score scaled to 0–100). A score of 100 means the two
        /// ingredients almost always appear together; 0 means no meaningful association.
        ///
        /// Pass <paramref name="ingredientId"/> to scope results to a single seed ingredient.
        /// Omit it to browse the strongest global pairings across the entire platform.
        /// When <paramref name="ingredientId"/> is supplied and not found, returns 404.
        /// </summary>
        /// <param name="ingredientId">Optional GUID of the seed ingredient. Returns pairings involving this ingredient.</param>
        /// <param name="minAffinity">Minimum affinity score (0–100) to include. Default <c>0</c>.</param>
        /// <param name="minCoOccurrence">Minimum number of co-occurrences to include. Default <c>0</c>.</param>
        /// <param name="region">Optional region name to scope pairings (e.g., <c>"Mediterranean"</c>).</param>
        /// <param name="cuisine">Optional cuisine type to scope pairings (e.g., <c>"Italian"</c>).</param>
        /// <param name="take">Number of results to return. Clamped to 1–200; default <c>50</c>.</param>
        /// <param name="sortBy">
        /// Sort order: <c>"affinity"</c> (default) sorts by the affinity score descending;
        /// <c>"cooccurrence"</c> sorts by raw co-occurrence count descending.
        /// </param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping pairings list and optional seed ingredient info.<br/>
        /// <c>400 Bad Request</c> — invalid <paramref name="sortBy"/> value.<br/>
        /// <c>404 Not Found</c> — <paramref name="ingredientId"/> was supplied but does not match any ingredient.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
