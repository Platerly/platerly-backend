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
        /// Get substitutions for an ingredient by ID — returns safety-wrapped response.
        /// </summary>
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
        /// Get substitutions for an ingredient by name — returns safety-wrapped response.
        /// </summary>
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
        /// Get all substitutions for a specific allergy by ID
        /// </summary>
        [HttpGet("allergy/{allergyId:guid}")]
        public async Task<ActionResult<List<SubstitutionDto>>> GetByAllergy(
            Guid allergyId,
            CancellationToken ct)
        {
            var substitutions = await _substitutionService.GetSubstitutionsByAllergyAsync(allergyId, ct);
            return Ok(substitutions);
        }

        /// <summary>
        /// Get all substitutions for a specific allergy by name
        /// </summary>
        [HttpGet("allergy/name/{allergyName}")]
        public async Task<ActionResult<List<SubstitutionDto>>> GetByAllergyName(
            string allergyName,
            CancellationToken ct)
        {
            var substitutions = await _substitutionService.GetSubstitutionsByAllergyNameAsync(allergyName, ct);
            return Ok(substitutions);
        }

        /// <summary>
        /// Get all substitutions by reason (Allergy, DietaryPreference, Religious, Health, etc.)
        /// </summary>
        [HttpGet("reason/{reason}")]
        public async Task<ActionResult<List<SubstitutionDto>>> GetByReason(
            string reason,
            CancellationToken ct)
        {
            var substitutions = await _substitutionService.GetSubstitutionsByReasonAsync(reason, ct);
            return Ok(substitutions);
        }

        /// <summary>
        /// Get top rated/used substitutions
        /// </summary>
        [HttpGet("top")]
        public async Task<ActionResult<List<SubstitutionDto>>> GetTop(
            [FromQuery] int take = 20,
            CancellationToken ct = default)
        {
            var substitutions = await _substitutionService.GetTopSubstitutionsAsync(take, ct);
            return Ok(substitutions);
        }

        /// <summary>
        /// Get bulk substitutions for multiple ingredients based on user allergies
        /// </summary>
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
        /// Suggest a new substitution (crowdsourced)
        /// </summary>
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
        /// Vote on a substitution (upvote or downvote)
        /// </summary>
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
        /// Record that a substitution was used (call this when user applies a substitution)
        /// </summary>
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
