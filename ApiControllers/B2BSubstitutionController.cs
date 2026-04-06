using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Models;
using Platerly.Api.Services.Ingredients;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Substitution Engine API.
    ///
    /// Exposes the full curated substitution database — including per-substitution
    /// <c>ConversionRatio</c>, <c>ConfidenceScore</c>, community <c>PositiveVotes</c>/<c>NegativeVotes</c>,
    /// <c>UsageCount</c>, and allergen/nickel safety annotations — to API key-authenticated partners.
    ///
    /// Base route: <c>api/b2b/substitution</c>
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
    ///       Scope <c>"substitution"</c> is required on every endpoint.
    ///
    /// All success responses are wrapped in <see cref="SafetyApiResponse{T}"/> via
    /// <see cref="B2BControllerBase.SafetyOk{T}"/>.
    ///
    /// Endpoints:
    ///   GET  /top                          — top N substitutions by usage + confidence
    ///   GET  /by-name                      — substitutions for a named ingredient, with optional allergen filtering
    ///   GET  /by-allergy                   — all substitutions for a named allergen
    ///   GET  /by-reason                    — all substitutions for a stated reason (Allergy / DietaryPreference / etc.)
    ///   POST /bulk                         — substitutions for up to 50 ingredients in one call
    ///   POST /{substitutionId:guid}/usage  — signal that a substitution was applied (increments UsageCount)
    /// </summary>
    [ApiController]
    [Route("api/b2b/substitution")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class B2BSubstitutionController : B2BControllerBase
    {
        private readonly IIngredientSubstitutionService _subs;
        private readonly ILogger<B2BSubstitutionController> _logger;

        public B2BSubstitutionController(
            IIngredientSubstitutionService subs,
            ILogger<B2BSubstitutionController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _subs = subs;
            _logger = logger;
        }

        // Returns a 403 ObjectResult when the "substitution" scope is absent, null otherwise.
        private IActionResult? ScopeGuard() =>
            HasScope("substitution") ? null :
            ApiError(
                "The 'substitution' scope is required for this endpoint. " +
                "Ensure your API key has the 'substitution' scope assigned, or contact api@recipzee.com.",
                403);

        /// <summary>
        /// <c>GET api/b2b/substitution/top</c> — retrieve the top N substitutions ranked by
        /// usage frequency and confidence score.
        ///
        /// Only substitutions that are verified (<c>IsVerified = true</c>) or have a confidence
        /// score ≥ 70 are included, ensuring results are community-validated.
        /// </summary>
        /// <param name="take">
        /// Number of results to return. Clamped to 1–100; default <c>20</c>.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping a <c>List&lt;SubstitutionDto&gt;</c>
        ///   ordered by <c>UsageCount</c> descending, then <c>ConfidenceScore</c> descending.<br/>
        /// <c>401</c> — missing or invalid API key.<br/>
        /// <c>403</c> — API key is valid but lacks the <c>substitution</c> scope.
        /// </returns>
        [HttpGet("top")]
        public async Task<IActionResult> GetTop(
            [FromQuery] int take = 20,
            CancellationToken ct = default)
        {
            var guard = ScopeGuard();
            if (guard != null) return guard;

            take = Math.Clamp(take, 1, 100);

            var results = await _subs.GetTopSubstitutionsAsync(take, ct);
            return SafetyOk(results);
        }

        /// <summary>
        /// <c>GET api/b2b/substitution/by-name</c> — retrieve substitutes for a specific ingredient
        /// specified by its display name.
        ///
        /// Name matching uses a two-step strategy:
        ///   1. Exact match on <c>NormalizedName</c> or alias.
        ///   2. Partial containment fallback (e.g., "Unsalted Butter" → "Butter").
        ///
        /// When <paramref name="allergyNames"/> is supplied:
        ///   • Substitution results are filtered to those relevant to the specified allergies.
        ///   • When "nickel" is among the allergy names, HIGH-nickel substitutes are excluded
        ///     and MODERATE-nickel substitutes receive a warning note in the response.
        /// </summary>
        /// <param name="ingredient">
        /// Display name of the ingredient to find substitutes for. Required.
        /// </param>
        /// <param name="allergyNames">
        /// Optional comma-separated list of allergy names to filter by (e.g., <c>"dairy,gluten,nickel"</c>).
        /// Names are matched case-insensitively against the Allergy table. Unknown names are silently skipped.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping a <c>List&lt;SubstitutionDto&gt;</c>
        ///   ordered by <c>ConfidenceScore</c> descending. Empty list if no match found.<br/>
        /// <c>400</c> — <paramref name="ingredient"/> is null or whitespace.<br/>
        /// <c>401</c> — missing or invalid API key.<br/>
        /// <c>403</c> — API key lacks the <c>substitution</c> scope.
        /// </returns>
        [HttpGet("by-name")]
        public async Task<IActionResult> GetByName(
            [FromQuery] string? ingredient,
            [FromQuery] string? allergyNames = null,
            CancellationToken ct = default)
        {
            var guard = ScopeGuard();
            if (guard != null) return guard;

            if (string.IsNullOrWhiteSpace(ingredient))
                return ApiError("'ingredient' query parameter is required.");

            List<Guid>? allergyIds = null;
            if (!string.IsNullOrWhiteSpace(allergyNames))
            {
                var names = allergyNames
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (names.Length > 0)
                    allergyIds = await _subs.ResolveAllergyNamesToIdsAsync(names, ct);
            }

            var results = await _subs.GetSubstitutionsForIngredientNameAsync(ingredient, allergyIds, ct);
            return SafetyOk(results);
        }

        /// <summary>
        /// <c>GET api/b2b/substitution/by-allergy</c> — retrieve all substitutions in the database
        /// that are tagged for a specific named allergy (e.g., <c>"dairy"</c>, <c>"gluten"</c>).
        ///
        /// This returns every substitution where the <c>AllergyId</c> foreign key resolves to the
        /// named allergy — useful for generating complete safe-ingredient swap tables per allergen.
        /// </summary>
        /// <param name="allergy">Allergy name. Case-insensitive. Required.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping <c>List&lt;SubstitutionDto&gt;</c>.
        ///   Empty list if no allergy or substitutions found.<br/>
        /// <c>400</c> — <paramref name="allergy"/> is null or whitespace.<br/>
        /// <c>401</c> — missing or invalid API key.<br/>
        /// <c>403</c> — API key lacks the <c>substitution</c> scope.
        /// </returns>
        [HttpGet("by-allergy")]
        public async Task<IActionResult> GetByAllergy(
            [FromQuery] string? allergy,
            CancellationToken ct = default)
        {
            var guard = ScopeGuard();
            if (guard != null) return guard;

            if (string.IsNullOrWhiteSpace(allergy))
                return ApiError("'allergy' query parameter is required.");

            var results = await _subs.GetSubstitutionsByAllergyNameAsync(allergy, ct);
            return SafetyOk(results);
        }

        /// <summary>
        /// <c>GET api/b2b/substitution/by-reason</c> — retrieve all substitutions tagged with
        /// a specific substitution reason.
        ///
        /// Valid reasons: <c>Allergy</c>, <c>DietaryPreference</c>, <c>Availability</c>,
        /// <c>Cost</c>, <c>Taste</c>, <c>Health</c>, <c>Religious</c>, <c>Other</c>.
        /// </summary>
        /// <param name="reason">
        /// Reason string. Case-insensitive. Must match a <see cref="SubstitutionReason"/> enum value.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping <c>List&lt;SubstitutionDto&gt;</c>.<br/>
        /// <c>400</c> — <paramref name="reason"/> is null/whitespace or not a valid enum value.<br/>
        /// <c>401</c> — missing or invalid API key.<br/>
        /// <c>403</c> — API key lacks the <c>substitution</c> scope.
        /// </returns>
        [HttpGet("by-reason")]
        public async Task<IActionResult> GetByReason(
            [FromQuery] string? reason,
            CancellationToken ct = default)
        {
            var guard = ScopeGuard();
            if (guard != null) return guard;

            if (string.IsNullOrWhiteSpace(reason))
                return ApiError("'reason' query parameter is required.");

            if (!Enum.TryParse<SubstitutionReason>(reason, ignoreCase: true, out _))
            {
                var valid = string.Join(", ", Enum.GetNames<SubstitutionReason>());
                return ApiError($"Invalid reason '{reason}'. Valid values: {valid}.");
            }

            var results = await _subs.GetSubstitutionsByReasonAsync(reason, ct);
            return SafetyOk(results);
        }

        /// <summary>
        /// <c>POST api/b2b/substitution/bulk</c> — retrieve substitutions for multiple ingredients
        /// in a single request. Maximum 50 ingredients per call.
        ///
        /// Each ingredient entry in the response includes:
        ///   • Whether it contains any of the specified allergens (<c>HasAllergen</c>).
        ///   • Which allergens were triggered (<c>TriggeredAllergies</c>).
        ///   • All available substitutions for that ingredient.
        ///
        /// Suitable for scanning a full recipe ingredient list in one API call.
        /// </summary>
        /// <param name="request">
        /// <c>IngredientNames</c>: 1–50 ingredient display name strings.<br/>
        /// <c>AllergyNames</c>: optional list of allergy names to filter by (e.g., <c>["dairy", "gluten"]</c>).
        ///   Names are matched case-insensitively; unknown names are silently skipped.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping <c>List&lt;BulkSubstitutionResponseDto&gt;</c>
        ///   with one entry per input ingredient, in input order.<br/>
        /// <c>400</c> — ingredient list is empty or exceeds 50 items.<br/>
        /// <c>401</c> — missing or invalid API key.<br/>
        /// <c>403</c> — API key lacks the <c>substitution</c> scope.
        /// </returns>
        [HttpPost("bulk")]
        public async Task<IActionResult> GetBulk(
            [FromBody] B2BBulkSubstitutionRequest request,
            CancellationToken ct = default)
        {
            var guard = ScopeGuard();
            if (guard != null) return guard;

            if (request.IngredientNames == null || request.IngredientNames.Count == 0)
                return ApiError("At least one ingredient name is required.");

            if (request.IngredientNames.Count > 50)
                return ApiError("A maximum of 50 ingredients per bulk request is allowed.");

            List<Guid>? allergyIds = null;
            if (request.AllergyNames != null && request.AllergyNames.Count > 0)
                allergyIds = await _subs.ResolveAllergyNamesToIdsAsync(request.AllergyNames, ct);

            var bulkRequest = new BulkSubstitutionRequestDto
            {
                IngredientNames = request.IngredientNames,
                UserAllergyIds = allergyIds
            };

            var results = await _subs.GetBulkSubstitutionsAsync(bulkRequest, ct);
            return SafetyOk(results);
        }

        /// <summary>
        /// <c>POST api/b2b/substitution/{substitutionId}/usage</c> — signal that a substitution
        /// was applied by an end-user, incrementing its <c>UsageCount</c>.
        ///
        /// This is a fire-and-forget telemetry call — always returns <c>204 No Content</c>
        /// even if the <paramref name="substitutionId"/> does not exist. Partners should call
        /// this after a user selects and applies a substitution from a previous lookup response,
        /// enabling the platform to surface the most-used substitutions via <c>GET /top</c>.
        /// </summary>
        /// <param name="substitutionId">GUID of the substitution that was applied.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>204 No Content</c> — always, regardless of whether the ID matched.<br/>
        /// <c>401</c> — missing or invalid API key.<br/>
        /// <c>403</c> — API key lacks the <c>substitution</c> scope.
        /// </returns>
        [HttpPost("{substitutionId:guid}/usage")]
        public async Task<IActionResult> TrackUsage(
            Guid substitutionId,
            CancellationToken ct = default)
        {
            var guard = ScopeGuard();
            if (guard != null) return guard;

            await _subs.IncrementUsageCountAsync(substitutionId, ct);
            return NoContent();
        }
    }

    /// <summary>
    /// Request body for <c>POST api/b2b/substitution/bulk</c>.
    /// Uses allergy names (strings) rather than Guids so partners do not need to
    /// pre-fetch Allergy IDs from a separate endpoint.
    /// </summary>
    public class B2BBulkSubstitutionRequest
    {
        /// <summary>
        /// Display names of the ingredients to look up. 1–50 entries required.
        /// </summary>
        public List<string> IngredientNames { get; set; } = new();

        /// <summary>
        /// Optional allergy names to filter and annotate results by
        /// (e.g., <c>["dairy", "gluten", "nickel"]</c>). Case-insensitive.
        /// </summary>
        public List<string>? AllergyNames { get; set; }
    }
}
