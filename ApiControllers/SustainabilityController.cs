using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Sustainability Scoring API — layer: B2B, base route: <c>api/Sustainability</c>.
    /// Intended to provide environmental impact scores for recipes based on per-ingredient
    /// carbon footprint, water usage, and land-use data.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
    ///
    /// <b>NOTE — feature not yet available.</b>
    /// The ingredient sustainability dataset is being compiled and validated.
    /// Both endpoints return HTTP <c>503 Service Unavailable</c> until the dataset is seeded.
    /// Estimated availability: Q3 2026. Subscribe to release notes at
    /// <see href="https://www.recipzee.com/api"/> for updates.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class SustainabilityController : B2BControllerBase
    {
        public SustainabilityController(ISafetyMetaService safetyMeta)
            : base(safetyMeta) { }

        // Shared response body for all "coming soon" endpoints.
        // Returned as-is with HTTP 503 so clients can parse a stable structure.
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
        /// <c>GET api/Sustainability/recipe/{recipeId}</c> — [Coming soon] Get sustainability score for a recipe.
        /// Will return an environmental impact score composed of carbon footprint (kg CO₂e),
        /// water usage (litres), and land-use (m²) aggregated across all recipe ingredients.
        /// </summary>
        /// <param name="recipeId">GUID of the recipe to score (ignored until feature is available).</param>
        /// <returns>
        /// <c>503 Service Unavailable</c> — always, until the sustainability dataset is released.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("recipe/{recipeId}")]
        public ActionResult GetRecipeSustainability(Guid recipeId)
            => StatusCode(503, ComingSoonBody);

        /// <summary>
        /// <c>POST api/Sustainability/compare</c> — [Coming soon] Compare sustainability scores across multiple recipes.
        /// Will accept a list of recipe IDs and return a ranked comparison of their environmental
        /// impact scores to help partners select more sustainable menu options.
        /// </summary>
        /// <returns>
        /// <c>503 Service Unavailable</c> — always, until the sustainability dataset is released.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpPost("compare")]
        public ActionResult CompareRecipes()
            => StatusCode(503, ComingSoonBody);
    }
}
