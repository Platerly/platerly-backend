using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Recipe Safety Audit API.
    /// Audit recipes for allergen risks, prohibited ingredients, and cross-contact warnings.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class RecipeSafetyController : ControllerBase
    {
        private readonly IRecipeSafetyService _safety;
        private readonly ILogger<RecipeSafetyController> _logger;

        public RecipeSafetyController(
            IRecipeSafetyService safety,
            ILogger<RecipeSafetyController> logger)
        {
            _safety = safety;
            _logger = logger;
        }

        /// <summary>
        /// Audit a single recipe for safety concerns.
        /// Submit an ingredient list and get allergen risks, prohibited flags, and cross-contact warnings.
        /// </summary>
        [HttpPost("audit")]
        public async Task<ActionResult<RecipeSafetyAuditResponse>> AuditRecipe(
            [FromBody] RecipeSafetyAuditRequest request,
            CancellationToken ct = default)
        {
            if (request.Ingredients == null || request.Ingredients.Count == 0)
            {
                return BadRequest("At least one ingredient is required");
            }

            if (request.Ingredients.Count > 200)
            {
                return BadRequest("Maximum 200 ingredients per audit");
            }

            var result = await _safety.AuditRecipeAsync(request, ct);
            return Ok(result);
        }

        /// <summary>
        /// Batch audit multiple recipes at once (max 50).
        /// Returns per-recipe breakdown and summary.
        /// </summary>
        [HttpPost("batch")]
        public async Task<ActionResult<BatchAuditResponse>> BatchAudit(
            [FromBody] BatchAuditRequest request,
            CancellationToken ct = default)
        {
            if (request.Recipes == null || request.Recipes.Count == 0)
            {
                return BadRequest("At least one recipe is required");
            }

            if (request.Recipes.Count > 50)
            {
                return BadRequest("Maximum 50 recipes per batch audit");
            }

            var result = await _safety.BatchAuditAsync(request, ct);
            return Ok(result);
        }
    }
}
