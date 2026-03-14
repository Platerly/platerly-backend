using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Services.Ingredients;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubstitutionController : ControllerBase
    {
        private readonly IIngredientSubstitutionService _substitutionService;
        private readonly ILogger<SubstitutionController> _logger;

        public SubstitutionController(
            IIngredientSubstitutionService substitutionService,
            ILogger<SubstitutionController> logger)
        {
            _substitutionService = substitutionService;
            _logger = logger;
        }

        /// <summary>
        /// Get substitutions for an ingredient by ID
        /// </summary>
        [HttpGet("ingredient/{ingredientId:guid}")]
        public async Task<ActionResult<List<SubstitutionDto>>> GetByIngredient(
            Guid ingredientId,
            CancellationToken ct)
        {
            var substitutions = await _substitutionService.GetSubstitutionsForIngredientAsync(ingredientId, ct);
            return Ok(substitutions);
        }

        /// <summary>
        /// Get substitutions for an ingredient by name
        /// </summary>
        [HttpGet("ingredient/name/{name}")]
        public async Task<ActionResult<List<SubstitutionDto>>> GetByIngredientName(
            string name,
            [FromQuery] List<Guid>? allergyIds,
            CancellationToken ct)
        {
            var substitutions = await _substitutionService.GetSubstitutionsForIngredientNameAsync(
                name, allergyIds, ct);
            return Ok(substitutions);
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
    }
}
