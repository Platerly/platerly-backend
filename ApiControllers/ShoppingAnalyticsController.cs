using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Shopping &amp; Meal Planning Analytics API — layer: B2B, base route: <c>api/ShoppingAnalytics</c>.
    /// Exposes aggregated, fully anonymised behavioural patterns derived from user shopping lists
    /// and meal plans. No individual user data is returned.
    /// Useful for grocery retailers, food delivery platforms, and meal-kit services seeking
    /// to understand what consumers are planning to buy and cook.
    /// All responses are wrapped in the Phase 2 <see cref="SafetyApiResponse{T}"/> envelope.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class ShoppingAnalyticsController : B2BControllerBase
    {
        private readonly IShoppingAnalyticsService _service;
        private readonly ILogger<ShoppingAnalyticsController> _logger;

        public ShoppingAnalyticsController(
            IShoppingAnalyticsService service,
            ILogger<ShoppingAnalyticsController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// <c>GET api/ShoppingAnalytics/patterns</c> — Get aggregated shopping list patterns.
        /// Returns the most frequently added ingredients ranked by addition count, together with
        /// platform-wide averages for list size and item check-off rate, and a category distribution
        /// showing which food groups dominate shopping lists.
        /// </summary>
        /// <param name="take">Number of top ingredients to return. Clamped to 1–200; default <c>50</c>.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the shopping pattern payload.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("patterns")]
        public async Task<ActionResult> GetPatterns(
            [FromQuery] int take = 50,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 200);
            var result = await _service.GetPatternsAsync(take, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// <c>GET api/ShoppingAnalytics/meal-planning</c> — Get meal planning behaviour patterns.
        /// Returns distribution across meal types (breakfast / lunch / dinner / snack),
        /// average plan duration in days, average number of recipes per plan,
        /// and the most commonly planned recipes across the platform.
        /// </summary>
        /// <param name="take">Number of top recipes to include in the popular-recipes list. Clamped to 1–100; default <c>20</c>.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the meal planning pattern payload.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
        [HttpGet("meal-planning")]
        public async Task<ActionResult> GetMealPlanning(
            [FromQuery] int take = 20,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 100);
            var result = await _service.GetMealPlanningPatternsAsync(take, ct);
            return SafetyOk(result);
        }
    }
}
