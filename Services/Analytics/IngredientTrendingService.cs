using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Analytics
{
    public class IngredientTrendingService : IIngredientTrendingService
    {
        private readonly PlaterlyDbContext _db;

        public IngredientTrendingService(PlaterlyDbContext db)
        {
            _db = db;
        }

        public async Task<IngredientTrendingResponse> GetTrendingAsync(
            IngredientTrendingRequest request,
            CancellationToken ct = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var from = today.AddDays(-request.Days);

            // ── Query IngredientTrendDaily over the window ──────────────────────

            var trendQuery = _db.IngredientTrendDaily
                .AsNoTracking()
                .Where(t => t.Date >= from && t.Date <= today);

            if (!string.IsNullOrWhiteSpace(request.Region))
                trendQuery = trendQuery.Where(t => t.Region == request.Region);
            else
                // Global rows have null region
                trendQuery = trendQuery.Where(t => t.Region == null);

            // Resolve trend direction filter to numeric
            int? directionFilter = request.Trend?.ToLower() switch
            {
                "rising" => 1,
                "declining" => -1,
                "stable" => 0,
                _ => null
            };

            // Aggregate per ingredient across the date range
            var aggregated = await trendQuery
                .GroupBy(t => t.GlobalIngredientId)
                .Select(g => new
                {
                    IngredientId = g.Key,
                    TotalUsage = g.Sum(t => t.UsageCount),
                    TotalRecipes = g.Max(t => t.UniqueRecipes),
                    TotalUsers = g.Max(t => t.UniqueUsers),
                    BestRank = g.Min(t => t.PopularityRank),
                    AvgDoD = g.Average(t => (decimal?)t.DayOverDayChange),
                    AvgWoW = g.Average(t => (decimal?)t.WeekOverWeekChange),
                    // Dominant direction: average — positive = rising, negative = declining
                    AvgDirection = g.Average(t => (double)t.TrendDirection)
                })
                .ToListAsync(ct);

            if (!aggregated.Any())
            {
                return new IngredientTrendingResponse
                {
                    Period = new IngredientTrendingPeriod
                    {
                        From = from,
                        To = today,
                        Days = request.Days,
                        Region = request.Region
                    }
                };
            }

            // Apply direction filter (done client-side after aggregation to keep SQL simple)
            var filtered = directionFilter.HasValue
                ? aggregated.Where(a =>
                    directionFilter.Value switch
                    {
                        1 => a.AvgDirection > 0.2,
                        -1 => a.AvgDirection < -0.2,
                        _ => a.AvgDirection >= -0.2 && a.AvgDirection <= 0.2
                    })
                : aggregated;

            // Sort
            var sorted = request.SortBy.ToLower() switch
            {
                "rank" => filtered.OrderBy(a => a.BestRank ?? int.MaxValue),
                "growth" => filtered.OrderByDescending(a => a.AvgWoW ?? 0),
                _ => filtered.OrderByDescending(a => a.TotalUsage)
            };

            var topIds = sorted
                .Take(request.Take)
                .Select(a => a.IngredientId)
                .ToHashSet();

            // ── Fetch ingredient metadata ───────────────────────────────────────

            var ingredients = await _db.GlobalIngredients
                .AsNoTracking()
                .Where(i => topIds.Contains(i.Id))
                .Select(i => new
                {
                    i.Id,
                    i.Name,
                    CategoryName = i.Category != null ? i.Category.Name : null,
                    NickelClass = i.NickelClassification.ToString()
                })
                .ToListAsync(ct);

            var ingredientMap = ingredients.ToDictionary(i => i.Id);

            // ── Fetch regional stats if region-filtered ─────────────────────────

            Dictionary<Guid, RegionalIngredientStats> regionalMap = new();
            if (!string.IsNullOrWhiteSpace(request.Region))
            {
                var regionalStats = await _db.RegionalIngredientStats
                    .AsNoTracking()
                    .Where(r => r.Region == request.Region && topIds.Contains(r.GlobalIngredientId))
                    .ToListAsync(ct);

                regionalMap = regionalStats.ToDictionary(r => r.GlobalIngredientId);
            }

            // ── Apply category filter (post-join) ──────────────────────────────

            // ── Build result DTOs ───────────────────────────────────────────────

            var resultItems = sorted
                .Take(request.Take)
                .Where(a => ingredientMap.ContainsKey(a.IngredientId))
                .Select(a =>
                {
                    var ing = ingredientMap[a.IngredientId];
                    double dir = a.AvgDirection;
                    string trendLabel = dir > 0.2 ? "rising" : dir < -0.2 ? "declining" : "stable";

                    IngredientRegionalStatsDto? regional = null;
                    if (regionalMap.TryGetValue(a.IngredientId, out var rs))
                    {
                        regional = new IngredientRegionalStatsDto
                        {
                            Region = rs.Region,
                            TotalUsageCount = rs.TotalUsageCount,
                            PenetrationRate = rs.PenetrationRate,
                            PopularityRank = rs.PopularityRank,
                            SeasonalPeakMonths = rs.SeasonalPeakMonths,
                            CommonDietaryContexts = rs.CommonDietaryContexts
                        };
                    }

                    return new B2BIngredientTrendDto
                    {
                        IngredientId = a.IngredientId,
                        Name = ing.Name,
                        Category = ing.CategoryName,
                        TotalUsageCount = a.TotalUsage,
                        UniqueRecipes = a.TotalRecipes,
                        UniqueUsers = a.TotalUsers,
                        PopularityRank = a.BestRank,
                        AvgDayOverDayChange = a.AvgDoD.HasValue ? Math.Round(a.AvgDoD.Value, 2) : null,
                        AvgWeekOverWeekChange = a.AvgWoW.HasValue ? Math.Round(a.AvgWoW.Value, 2) : null,
                        TrendDirection = trendLabel,
                        NickelClassification = ing.NickelClass,
                        RegionalStats = regional
                    };
                })
                .ToList();

            // Apply category filter now that we have names
            if (!string.IsNullOrWhiteSpace(request.Category))
            {
                resultItems = resultItems
                    .Where(i => string.Equals(i.Category, request.Category, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return new IngredientTrendingResponse
            {
                Period = new IngredientTrendingPeriod
                {
                    From = from,
                    To = today,
                    Days = request.Days,
                    Region = request.Region
                },
                Ingredients = resultItems
            };
        }
    }
}
