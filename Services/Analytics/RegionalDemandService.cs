using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>
    /// B2B data layer — regional ingredient demand forecasting and region comparison analytics.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Queries <c>IngredientTrendDaily</c> over two equal-length periods (current and previous)
    /// to compute demand change (%) for each ingredient by region. Supports category filtering.
    /// </summary>
    public class RegionalDemandService : IRegionalDemandService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<RegionalDemandService> _logger;

        public RegionalDemandService(
            PlaterlyDbContext db,
            ILogger<RegionalDemandService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Returns ingredient demand trends for a region over a configurable day window.
        /// Compares the current period against an equal-length prior period to compute demand change.
        /// </summary>
        /// <param name="request">Region, optional category filter, day window, and take count.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A <see cref="RegionalDemandForecastResponse"/> with demand-change ranked ingredients.</returns>
        public async Task<RegionalDemandForecastResponse> GetForecastAsync(
            RegionalDemandForecastRequest request,
            CancellationToken ct = default)
        {
            var endDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var midDate = endDate.AddDays(-request.Days);
            var startDate = midDate.AddDays(-request.Days);

            var query = _db.IngredientTrendDaily
                .Include(t => t.GlobalIngredient)
                    .ThenInclude(g => g.Category)
                .Where(t => t.Date >= startDate && t.Date <= endDate);

            if (!string.IsNullOrEmpty(request.Region))
            {
                query = query.Where(t => t.Region == request.Region);
            }

            if (!string.IsNullOrEmpty(request.Category))
            {
                query = query.Where(t => t.GlobalIngredient.Category != null &&
                    t.GlobalIngredient.Category.Slug == request.Category);
            }

            var grouped = await query
                .GroupBy(t => new
                {
                    t.GlobalIngredientId,
                    IngredientName = t.GlobalIngredient.Name,
                    Category = t.GlobalIngredient.Category != null ? t.GlobalIngredient.Category.Name : null,
                    Region = t.Region ?? "Global"
                })
                .Select(g => new
                {
                    g.Key.GlobalIngredientId,
                    g.Key.IngredientName,
                    g.Key.Category,
                    g.Key.Region,
                    CurrentPeriod = g.Where(t => t.Date >= midDate).Sum(t => t.UsageCount),
                    PreviousPeriod = g.Where(t => t.Date < midDate).Sum(t => t.UsageCount)
                })
                .OrderByDescending(x => x.CurrentPeriod)
                .Take(request.Take)
                .ToListAsync(ct);

            var forecasts = grouped.Select(g =>
            {
                var growthRate = g.PreviousPeriod > 0
                    ? Math.Round((decimal)(g.CurrentPeriod - g.PreviousPeriod) / g.PreviousPeriod * 100, 2)
                    : g.CurrentPeriod > 0 ? 100m : 0m;

                return new RegionalDemandForecastDto
                {
                    GlobalIngredientId = g.GlobalIngredientId,
                    IngredientName = g.IngredientName,
                    Category = g.Category,
                    Region = g.Region,
                    CurrentPeriodUsage = g.CurrentPeriod,
                    PreviousPeriodUsage = g.PreviousPeriod,
                    GrowthRate = growthRate,
                    Velocity = g.CurrentPeriod > 0
                        ? Math.Round((decimal)g.CurrentPeriod / request.Days, 2)
                        : 0m,
                    Trend = growthRate > 10 ? "Rising" : growthRate < -10 ? "Declining" : "Stable"
                };
            }).ToList();

            return new RegionalDemandForecastResponse
            {
                Region = request.Region,
                AnalysisDays = request.Days,
                Forecasts = forecasts,
                TotalIngredients = forecasts.Count
            };
        }

        public async Task<RegionalComparisonResponse> CompareRegionsAsync(
            RegionalComparisonRequest request,
            CancellationToken ct = default)
        {
            var regionList = request.Regions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (regionList.Count < 2)
            {
                return new RegionalComparisonResponse
                {
                    Regions = regionList,
                    Ingredients = new()
                };
            }

            var query = _db.RegionalIngredientStats
                .Include(r => r.GlobalIngredient)
                .Where(r => regionList.Contains(r.Region));

            if (request.IngredientId.HasValue)
            {
                query = query.Where(r => r.GlobalIngredientId == request.IngredientId.Value);
            }

            var stats = await query.ToListAsync(ct);

            var grouped = stats
                .GroupBy(s => new { s.GlobalIngredientId, s.GlobalIngredient.Name })
                .Take(request.Take)
                .Select(g => new RegionalComparisonIngredientDto
                {
                    GlobalIngredientId = g.Key.GlobalIngredientId,
                    IngredientName = g.Key.Name,
                    UsageByRegion = g.ToDictionary(s => s.Region, s => s.TotalUsageCount),
                    RankByRegion = g.ToDictionary(s => s.Region, s => s.PopularityRank)
                })
                .ToList();

            return new RegionalComparisonResponse
            {
                Regions = regionList,
                Ingredients = grouped
            };
        }
    }
}
