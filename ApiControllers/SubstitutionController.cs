using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Models;
using Platerly.Api.Services.Ingredients;
using Platerly.Api.Services.Safety;
using System.Diagnostics;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2C/public controller for the ingredient substitution engine.
    /// Exposes read endpoints for finding safe alternatives to allergen-containing ingredients
    /// (by ID, name, allergy, or reason), plus crowdsourced suggestion and voting.
    /// Every substitution response that involves allergy context is wrapped in a
    /// <see cref="SafetyApiResponse{T}"/> envelope containing safety metadata and audit logs.
    /// Base route: <c>api/substitution</c>.
    /// Read endpoints are public; suggestion and vote endpoints require a valid JWT.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SubstitutionController : ControllerBase
    {
        private readonly IIngredientSubstitutionService _substitutionService;
        private readonly ISafetyMetaService _safetyMeta;
        private readonly ISafetyAuditService _safetyAudit;
        private readonly ILogger<SubstitutionController> _logger;

        public SubstitutionController(
            IIngredientSubstitutionService substitutionService,
            ISafetyMetaService safetyMeta,
            ISafetyAuditService safetyAudit,
            ILogger<SubstitutionController> logger)
        {
            _substitutionService = substitutionService;
            _safetyMeta = safetyMeta;
            _safetyAudit = safetyAudit;
            _logger = logger;
        }

        /// <summary>
        /// Retrieve substitutions for a specific ingredient looked up by its GUID.
        /// The response is wrapped in a <see cref="SafetyApiResponse{T}"/> with safety metadata
        /// (alerts, confidence level). The call is timed and asynchronously logged to the safety
        /// audit table (fire-and-forget).
        /// </summary>
        /// <param name="ingredientId">The global ingredient GUID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>SafetyApiResponse&lt;List&lt;SubstitutionDto&gt;&gt;</c>.
        /// </returns>
        [HttpGet("ingredient/{ingredientId:guid}")]
        public async Task<ActionResult<SafetyApiResponse<List<SubstitutionDto>>>> GetByIngredient(
            Guid ingredientId,
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var substitutions = await _substitutionService.GetSubstitutionsForIngredientAsync(ingredientId, ct);
            var meta = _safetyMeta.BuildForSubstitutions(substitutions);
            sw.Stop();

            _ = _safetyAudit.LogAsync(BuildAuditEntry(
                ingredientId.ToString(), null, meta, sw.ElapsedMilliseconds, 200), ct);

            return Ok(new SafetyApiResponse<List<SubstitutionDto>> { Data = substitutions, Safety = meta });
        }

        /// <summary>
        /// Retrieve substitutions for an ingredient looked up by its display name.
        /// Optionally filters substitutions by a list of allergy IDs to exclude unsafe alternatives.
        /// Result is wrapped in a safety envelope and logged to the audit table.
        /// </summary>
        /// <param name="name">The ingredient name (URL-encoded if necessary).</param>
        /// <param name="allergyIds">Optional list of allergy GUIDs to filter out unsafe substitutes.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>SafetyApiResponse&lt;List&lt;SubstitutionDto&gt;&gt;</c>.
        /// </returns>
        [HttpGet("ingredient/name/{name}")]
        public async Task<ActionResult<SafetyApiResponse<List<SubstitutionDto>>>> GetByIngredientName(
            string name,
            [FromQuery] List<Guid>? allergyIds,
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var substitutions = await _substitutionService.GetSubstitutionsForIngredientNameAsync(
                name, allergyIds, ct);
            var meta = _safetyMeta.BuildForSubstitutions(substitutions);
            sw.Stop();

            _ = _safetyAudit.LogAsync(BuildAuditEntry(
                name, allergyIds?.Select(id => id.ToString()).ToList(), meta, sw.ElapsedMilliseconds, 200), ct);

            return Ok(new SafetyApiResponse<List<SubstitutionDto>> { Data = substitutions, Safety = meta });
        }

        /// <summary>
        /// Retrieve all substitutions that are relevant for a given allergy (looked up by GUID).
        /// Useful for generating a complete safe-swap list for a single allergen.
        /// </summary>
        /// <param name="allergyId">The allergy GUID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — array of <see cref="SubstitutionDto"/>.</returns>
        [HttpGet("allergy/{allergyId:guid}")]
        public async Task<ActionResult<List<SubstitutionDto>>> GetByAllergy(
            Guid allergyId,
            CancellationToken ct)
        {
            var substitutions = await _substitutionService.GetSubstitutionsByAllergyAsync(allergyId, ct);
            return Ok(substitutions);
        }

        /// <summary>
        /// Retrieve all substitutions for a given allergy looked up by its canonical name
        /// (e.g., "Milk", "Gluten", "Nickel").
        /// </summary>
        /// <param name="allergyName">The allergy name (case-insensitive in the service layer).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — array of <see cref="SubstitutionDto"/>.</returns>
        [HttpGet("allergy/name/{allergyName}")]
        public async Task<ActionResult<List<SubstitutionDto>>> GetByAllergyName(
            string allergyName,
            CancellationToken ct)
        {
            var substitutions = await _substitutionService.GetSubstitutionsByAllergyNameAsync(allergyName, ct);
            return Ok(substitutions);
        }

        /// <summary>
        /// Retrieve all substitutions filtered by their substitution reason category.
        /// Valid reasons include <c>Allergy</c>, <c>DietaryPreference</c>, <c>Religious</c>,
        /// <c>Health</c>, and others defined in the service.
        /// </summary>
        /// <param name="reason">The reason string (case-insensitive).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — array of <see cref="SubstitutionDto"/>.</returns>
        [HttpGet("reason/{reason}")]
        public async Task<ActionResult<List<SubstitutionDto>>> GetByReason(
            string reason,
            CancellationToken ct)
        {
            var substitutions = await _substitutionService.GetSubstitutionsByReasonAsync(reason, ct);
            return Ok(substitutions);
        }

        /// <summary>
        /// Retrieve the top substitutions ranked by usage count / rating.
        /// Useful for a "popular swaps" feature or onboarding suggestions.
        /// </summary>
        /// <param name="take">Maximum results to return (default 20).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>200 OK</c> — array of <see cref="SubstitutionDto"/> ordered by rank.</returns>
        [HttpGet("top")]
        public async Task<ActionResult<List<SubstitutionDto>>> GetTop(
            [FromQuery] int take = 20,
            CancellationToken ct = default)
        {
            var substitutions = await _substitutionService.GetTopSubstitutionsAsync(take, ct);
            return Ok(substitutions);
        }

        /// <summary>
        /// Retrieve substitutions for multiple ingredients in a single request.
        /// Typically called after a recipe's ingredient list is loaded, passing the user's
        /// allergy IDs to restrict results to safe alternatives only.
        /// </summary>
        /// <param name="request">
        /// Contains <c>IngredientNames</c> (required, at least one) and optional <c>AllergyIds</c>.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — array of <see cref="BulkSubstitutionResponseDto"/> (one entry per ingredient).<br/>
        /// <c>400</c> — no ingredient names provided.
        /// </returns>
        [HttpPost("bulk")]
        public async Task<ActionResult<List<BulkSubstitutionResponseDto>>> GetBulkSubstitutions(
            [FromBody] BulkSubstitutionRequestDto request,
            CancellationToken ct)
        {
            if (request.IngredientNames == null || !request.IngredientNames.Any())
            {
                return BadRequest("At least one ingredient name is required");
            }

            var results = await _substitutionService.GetBulkSubstitutionsAsync(request, ct);
            return Ok(results);
        }

        /// <summary>
        /// Submit a crowdsourced substitution suggestion for review.
        /// The suggestion is associated with the authenticated user. If the same substitution
        /// already exists, the service throws <see cref="InvalidOperationException"/> which is
        /// mapped to a 400 response.
        /// </summary>
        /// <param name="dto">
        /// Substitution details: original ingredient name, substitute ingredient name, reason, notes.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>201 Created</c> — the created <see cref="SubstitutionDto"/>.<br/>
        /// <c>400</c> — duplicate or invalid data.<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<SubstitutionDto>> SuggestSubstitution(
            [FromBody] SuggestSubstitutionDto dto,
            CancellationToken ct)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized();
            }

            try
            {
                var substitution = await _substitutionService.SuggestSubstitutionAsync(dto, userId, ct);
                return CreatedAtAction(
                    nameof(GetByIngredientName),
                    new { name = dto.OriginalIngredientName },
                    substitution);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Cast an upvote or downvote on a substitution, with an optional comment.
        /// Votes are associated with the authenticated user. If the substitution does not
        /// exist the service throws <see cref="InvalidOperationException"/> which is mapped to 404.
        /// </summary>
        /// <param name="substitutionId">The substitution GUID to vote on.</param>
        /// <param name="dto">
        /// Contains <c>IsPositive</c> (true = upvote, false = downvote) and optional <c>Comment</c>.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — updated <see cref="SubstitutionDto"/> with new vote counts.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — substitution not found.
        /// </returns>
        [HttpPost("{substitutionId:guid}/vote")]
        [Authorize]
        public async Task<ActionResult<SubstitutionDto>> Vote(
            Guid substitutionId,
            [FromBody] VoteSubstitutionDto dto,
            CancellationToken ct)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized();
            }

            try
            {
                var substitution = await _substitutionService.VoteOnSubstitutionAsync(
                    substitutionId, userId, dto.IsPositive, dto.Comment, ct);
                return Ok(substitution);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }

        /// <summary>
        /// Increment the usage counter for a substitution. Call this endpoint when the user
        /// confirms they have applied the substitution to a recipe. No authentication required;
        /// the counter is used for ranking in the <c>GET /top</c> endpoint.
        /// </summary>
        /// <param name="substitutionId">The substitution GUID that was applied.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>204 No Content</c>.</returns>
        [HttpPost("{substitutionId:guid}/used")]
        public async Task<IActionResult> RecordUsage(Guid substitutionId, CancellationToken ct)
        {
            await _substitutionService.IncrementUsageCountAsync(substitutionId, ct);
            return NoContent();
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private SafetyAuditLog BuildAuditEntry(
            string ingredientContext,
            List<string>? allergyContext,
            SafetyMeta meta,
            long elapsedMs,
            int statusCode)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid.TryParse(userId, out var parsedUserId);

            string? highestSeverity = null;
            if (meta.Alerts.Any(a => a.Severity == "FATAL")) highestSeverity = "FATAL";
            else if (meta.Alerts.Any(a => a.Severity == "WARNING")) highestSeverity = "WARNING";
            else if (meta.Alerts.Any()) highestSeverity = "INFO";

            return new SafetyAuditLog
            {
                TimestampUtc = DateTime.UtcNow,
                HttpMethod = HttpContext.Request.Method,
                Endpoint = HttpContext.Request.Path,
                UserId = parsedUserId == Guid.Empty ? null : parsedUserId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString().TruncateTo(512),
                ResponseStatusCode = statusCode,
                ResponseTimeMs = elapsedMs,
                AlertCount = meta.Alerts.Count,
                HighestAlertSeverity = highestSeverity,
                IngredientContext = ingredientContext.TruncateTo(1024),
                AllergyContext = allergyContext != null
                    ? string.Join(", ", allergyContext).TruncateTo(512)
                    : null,
                ConfidenceLevel = meta.Confidence.Level
            };
        }
    }

    internal static class StringExtensions
    {
        public static string TruncateTo(this string s, int maxLength)
            => s.Length <= maxLength ? s : s[..maxLength];
    }
}
