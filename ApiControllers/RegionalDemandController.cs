using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Regional Demand Forecasting API.
    /// Compare ingredient demand across regions and forecast growth trends.
    /// Requires API Key authentication.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class RegionalDemandController : B2BControllerBase
    {
        private readonly IRegionalDemandService _service;
        private readonly ILogger<RegionalDemandController> _logger;

        public RegionalDemandController(
            IRegionalDemandService service,
            ILogger<RegionalDemandController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// Get ingredient demand forecast by region.
        /// Compares current vs previous period to calculate growth velocity.
        /// </summary>
        /// <param name="region">Optional region filter</param>
        /// <param name="days">Analysis period in days (default 30, max 180)</param>
        /// <param name="take">Number of results (default 50, max 200)</param>
        /// <param name="category">Optional category slug filter</param>
        [HttpGet("forecast")]
        public async Task<ActionResult> GetForecast(
            [FromQuery] string? region = null,
            [FromQuery] int days = 30,
            [FromQuery] int take = 50,
            [FromQuery] string? category = null,
            CancellationToken ct = default)
        {
            days = Math.Clamp(days, 7, 180);
            take = Math.Clamp(take, 1, 200);

            var request = new RegionalDemandForecastRequest
            {
                Region = region,
                Days = days,
                Take = take,
                Category = category
            };

            var result = await _service.GetForecastAsync(request, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// Compare ingredient popularity across multiple regions.
        /// </summary>
        /// <param name="regions">Comma-separated region names (at least 2)</param>
        /// <param name="ingredientId">Optional specific ingredient to compare</param>
        /// <param name="take">Number of ingredients to compare (default 20, max 100)</param>
        [HttpGet("compare")]
        public async Task<ActionResult> CompareRegions(
            [FromQuery] string regions,
            [FromQuery] Guid? ingredientId = null,
            [FromQuery] int take = 20,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(regions))
                return ApiError("At least 2 comma-separated regions are required.");

            take = Math.Clamp(take, 1, 100);

            var request = new RegionalComparisonRequest
            {
                Regions = regions,
                IngredientId = ingredientId,
                Take = take
            };

            var result = await _service.CompareRegionsAsync(request, ct);
            return SafetyOk(result);
        }
    }
}
