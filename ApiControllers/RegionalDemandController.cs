using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Regional Demand Forecasting API — layer: B2B, base route: <c>api/RegionalDemand</c>.
    /// Analyses ingredient demand trends from recipe creation activity, broken down by region.
    /// Provides growth-velocity forecasting (current vs. prior period) and side-by-side
    /// regional comparisons for one or more ingredients.
    /// All responses are wrapped in the Phase 2 <see cref="SafetyApiResponse{T}"/> envelope.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
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
        /// <c>GET api/RegionalDemand/forecast</c> — Get ingredient demand forecast by region.
        /// Splits the requested look-back window into two equal halves (current vs. prior period)
        /// and computes a growth-velocity score for each ingredient, indicating whether demand
        /// is rising, stable, or declining in the specified region.
        /// </summary>
        /// <param name="region">Optional region name to scope results (e.g., <c>"North America"</c>). Omit for global data.</param>
        /// <param name="days">Analysis look-back window in days. Clamped to 7–180; default <c>30</c>.</param>
        /// <param name="take">Maximum number of ingredients to return. Clamped to 1–200; default <c>50</c>.</param>
        /// <param name="category">Optional ingredient category slug to narrow results (e.g., <c>"vegetables"</c>).</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the forecast list.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
        /// <c>GET api/RegionalDemand/compare</c> — Compare ingredient popularity across multiple regions.
        /// Pass at least two comma-separated region names to get a side-by-side usage count
        /// and rank for each ingredient across all specified regions.
        /// Optionally scope the comparison to a single ingredient via <paramref name="ingredientId"/>.
        /// </summary>
        /// <param name="regions">
        /// Comma-separated region names — at least 2 required (e.g., <c>"North America,Europe"</c>).
        /// </param>
        /// <param name="ingredientId">Optional GUID to scope the comparison to one specific ingredient.</param>
        /// <param name="take">Number of ingredients to include in the comparison. Clamped to 1–100; default <c>20</c>.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the cross-region comparison.<br/>
        /// <c>400 Bad Request</c> — <paramref name="regions"/> is null or whitespace.<br/>
        /// <c>401 Unauthorized</c> — missing or invalid API key.
        /// </returns>
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
