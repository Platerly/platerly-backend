using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Sustainability Scoring API.
    /// Environmental impact scoring for recipes based on ingredient sustainability data.
    /// Requires API Key authentication.
    ///
    /// NOTE: This feature is not yet available. The sustainability database is under active
    /// development. Both endpoints return 503 until the dataset is seeded and validated.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class SustainabilityController : B2BControllerBase
    {
        public SustainabilityController(ISafetyMetaService safetyMeta)
            : base(safetyMeta) { }

        private static readonly object ComingSoonBody = new
        {
            status = 503,
            code = "FEATURE_NOT_YET_AVAILABLE",
            message = "Sustainability scoring is under development. " +
                      "The ingredient sustainability dataset is being compiled and validated. " +
                      "This endpoint will be available in a future release. " +
                      "Subscribe to release notes at https://www.recipzee.com/api for updates.",
            estimatedAvailability = "Q3 2026"
        };

        /// <summary>
        /// [Coming soon] Get sustainability score for a recipe.
        /// Returns 503 until the sustainability dataset is available.
        /// </summary>
        [HttpGet("recipe/{recipeId}")]
        public ActionResult GetRecipeSustainability(Guid recipeId)
            => StatusCode(503, ComingSoonBody);

        /// <summary>
        /// [Coming soon] Compare sustainability scores across multiple recipes.
        /// Returns 503 until the sustainability dataset is available.
        /// </summary>
        [HttpPost("compare")]
        public ActionResult CompareRecipes()
            => StatusCode(503, ComingSoonBody);
    }
}
