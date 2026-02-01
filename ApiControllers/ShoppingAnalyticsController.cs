using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Shopping &amp; Meal Planning Analytics API.
    /// Aggregated, anonymized patterns from shopping lists and meal plans.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class ShoppingAnalyticsController : ControllerBase
    {
        private readonly IShoppingAnalyticsService _service;
        private readonly ILogger<ShoppingAnalyticsController> _logger;

        public ShoppingAnalyticsController(
            IShoppingAnalyticsService service,
            ILogger<ShoppingAnalyticsController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// Get aggregated shopping list patterns.
        /// Most added ingredients, average list size, checked-off rates, category distribution.
        /// </summary>
        /// <param name="take">Number of top ingredients to return (default 50, max 200)</param>
        [HttpGet("patterns")]
        public async Task<ActionResult<ShoppingPatternsResponse>> GetPatterns(
            [FromQuery] int take = 50,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 200);
            var result = await _service.GetPatternsAsync(take, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get meal planning patterns.
        /// Meal type distribution, average plan duration, recipes per plan, popular recipes.
        /// </summary>
        /// <param name="take">Number of top recipes to return (default 20, max 100)</param>
        [HttpGet("meal-planning")]
        public async Task<ActionResult<MealPlanningPatternsResponse>> GetMealPlanning(
            [FromQuery] int take = 20,
            CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, 100);
            var result = await _service.GetMealPlanningPatternsAsync(take, ct);
            return Ok(result);
        }
    }
}
