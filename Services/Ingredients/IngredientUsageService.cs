using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    public class IngredientUsageService : IIngredientUsageService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<IngredientUsageService> _logger;

        public IngredientUsageService(
            PlaterlyDbContext db,
            ILogger<IngredientUsageService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task LogRecipeIngredientsAsync(
            Guid recipeId,
            Guid userId,
            IEnumerable<Ingredient> ingredients,
            string? ipAddress = null,
            string? userAgent = null,
            CancellationToken ct = default)
        {
            // Get geo info from IP (simplified - in production use a geo service)
            var (region, countryCode, city) = GetGeoFromIp(ipAddress);

            // Get recipe tags for dietary context
            var recipeTags = await _db.RecipeTags
                .Where(rt => rt.RecipeId == recipeId)
                .Include(rt => rt.Tag)
                .Select(rt => rt.Tag.Name)
                .ToListAsync(ct);

            var dietaryTags = string.Join(",", recipeTags);

            var usageLogs = new List<IngredientUsageLog>();
            var ingredientIds = new List<Guid>();

            foreach (var ingredient in ingredients)
            {
                var log = new IngredientUsageLog
                {
                    GlobalIngredientId = ingredient.GlobalIngredientId,
                    RecipeId = recipeId,
                    UserId = userId,
                    Quantity = ingredient.Quantity,
                    QuantityNumeric = ParseQuantity(ingredient.Quantity),
                    Unit = ExtractUnit(ingredient.Quantity),
                    Region = region,
                    CountryCode = countryCode,
                    City = city,
                    DietaryTags = dietaryTags,
                    CreatedAt = DateTime.UtcNow
                };

                usageLogs.Add(log);
                ingredientIds.Add(ingredient.GlobalIngredientId);

                // Update global ingredient usage count
                await _db.GlobalIngredients
                    .Where(g => g.Id == ingredient.GlobalIngredientId)
                    .ExecuteUpdateAsync(g => g
                        .SetProperty(x => x.TotalUsageCount, x => x.TotalUsageCount + 1)
                        .SetProperty(x => x.LastUsedAt, DateTime.UtcNow),
                        ct);
            }

            if (usageLogs.Any())
            {
                await _db.IngredientUsageLogs.AddRangeAsync(usageLogs, ct);
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Logged {Count} ingredient usages for recipe {RecipeId}",
                    usageLogs.Count, recipeId);

                // Update pairings in background (don't await)
                _ = UpdatePairingsForRecipeAsync(recipeId, ingredientIds, CancellationToken.None);
            }
        }

        public async Task LogIngredientUsageAsync(
            Guid globalIngredientId,
            Guid recipeId,
            Guid userId,
            string? quantity = null,
            string? unit = null,
            string? region = null,
            string? countryCode = null,
            string? city = null,
            string? dietaryTags = null,
            CancellationToken ct = default)
        {
            var log = new IngredientUsageLog
            {
                GlobalIngredientId = globalIngredientId,
                RecipeId = recipeId,
                UserId = userId,
                Quantity = quantity,
                QuantityNumeric = ParseQuantity(quantity),
                Unit = unit,
                Region = region,
                CountryCode = countryCode,
                City = city,
                DietaryTags = dietaryTags,
                CreatedAt = DateTime.UtcNow
            };

            _db.IngredientUsageLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }

        public async Task LogSearchAsync(
            string query,
            int resultCount,
            string? region = null,
            string? searchContext = null,
            CancellationToken ct = default)
        {
            var log = new IngredientSearchLog
            {
                Query = query.Length > 256 ? query[..256] : query,
                WasFound = resultCount > 0,
                Region = region,
                SearchContext = searchContext,
                CreatedAt = DateTime.UtcNow
            };

            _db.IngredientSearchLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }

        public async Task RecomputeDailyTrendsAsync(DateOnly date, CancellationToken ct = default)
        {
            var startOfDay = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var endOfDay = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

            // Get usage counts for the day grouped by ingredient and region
            var dailyUsage = await _db.IngredientUsageLogs
                .Where(l => l.CreatedAt >= startOfDay && l.CreatedAt <= endOfDay)
                .GroupBy(l => new { l.GlobalIngredientId, l.Region })
                .Select(g => new
                {
                    g.Key.GlobalIngredientId,
                    g.Key.Region,
                    UsageCount = g.Count(),
                    UniqueRecipes = g.Select(x => x.RecipeId).Distinct().Count(),
                    UniqueUsers = g.Select(x => x.UserId).Distinct().Count()
                })
                .ToListAsync(ct);

            // Get previous day for comparison
            var previousDate = date.AddDays(-1);
            var previousStart = previousDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var previousEnd = previousDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

            var previousUsage = await _db.IngredientUsageLogs
                .Where(l => l.CreatedAt >= previousStart && l.CreatedAt <= previousEnd)
                .GroupBy(l => new { l.GlobalIngredientId, l.Region })
                .Select(g => new { g.Key.GlobalIngredientId, g.Key.Region, UsageCount = g.Count() })
                .ToDictionaryAsync(x => (x.GlobalIngredientId, x.Region ?? ""), x => x.UsageCount, ct);

            // Get week ago for comparison
            var weekAgoDate = date.AddDays(-7);
            var weekAgoStart = weekAgoDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var weekAgoEnd = weekAgoDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

            var weekAgoUsage = await _db.IngredientUsageLogs
                .Where(l => l.CreatedAt >= weekAgoStart && l.CreatedAt <= weekAgoEnd)
                .GroupBy(l => new { l.GlobalIngredientId, l.Region })
                .Select(g => new { g.Key.GlobalIngredientId, g.Key.Region, UsageCount = g.Count() })
                .ToDictionaryAsync(x => (x.GlobalIngredientId, x.Region ?? ""), x => x.UsageCount, ct);

            var trends = new List<IngredientTrendDaily>();

            foreach (var usage in dailyUsage)
            {
                var key = (usage.GlobalIngredientId, usage.Region ?? "");
                previousUsage.TryGetValue(key, out var prevCount);
                weekAgoUsage.TryGetValue(key, out var weekCount);

                var dayOverDay = prevCount > 0
                    ? (decimal)(usage.UsageCount - prevCount) / prevCount * 100
                    : 0;
                var weekOverWeek = weekCount > 0
                    ? (decimal)(usage.UsageCount - weekCount) / weekCount * 100
                    : 0;

                // Determine trend direction
                int trendDirection = 0;
                if (dayOverDay > 5) trendDirection = 1;
                else if (dayOverDay < -5) trendDirection = -1;

                trends.Add(new IngredientTrendDaily
                {
                    GlobalIngredientId = usage.GlobalIngredientId,
                    Date = date,
                    Region = usage.Region,
                    UsageCount = usage.UsageCount,
                    UniqueRecipes = usage.UniqueRecipes,
                    UniqueUsers = usage.UniqueUsers,
                    DayOverDayChange = dayOverDay,
                    WeekOverWeekChange = weekOverWeek,
                    TrendDirection = trendDirection,
                    ComputedAt = DateTime.UtcNow
                });
            }

            // Remove existing trends for this date
            await _db.IngredientTrendDaily
                .Where(t => t.Date == date)
                .ExecuteDeleteAsync(ct);

            // Add new trends
            await _db.IngredientTrendDaily.AddRangeAsync(trends, ct);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Computed {Count} daily trends for {Date}", trends.Count, date);
        }

        public async Task RecomputeRegionalStatsAsync(string region, CancellationToken ct = default)
        {
            // Get all usage data for the region
            var usageData = await _db.IngredientUsageLogs
                .Where(l => l.Region == region)
                .GroupBy(l => l.GlobalIngredientId)
                .Select(g => new
                {
                    GlobalIngredientId = g.Key,
                    TotalUsageCount = g.LongCount(),
                    UniqueRecipeCount = g.Select(x => x.RecipeId).Distinct().Count(),
                    AverageQuantity = g.Where(x => x.QuantityNumeric != null).Average(x => x.QuantityNumeric),
                    CommonUnit = g.Where(x => x.Unit != null).GroupBy(x => x.Unit)
                        .OrderByDescending(ug => ug.Count()).Select(ug => ug.Key).FirstOrDefault(),
                    Months = g.Select(x => x.CreatedAt.Month).ToList()
                })
                .ToListAsync(ct);

            var totalRecipesInRegion = await _db.IngredientUsageLogs
                .Where(l => l.Region == region)
                .Select(l => l.RecipeId)
                .Distinct()
                .CountAsync(ct);

            var stats = new List<RegionalIngredientStats>();
            int rank = 0;

            // Sort by usage count for ranking
            var sortedData = usageData.OrderByDescending(d => d.TotalUsageCount).ToList();

            foreach (var data in sortedData)
            {
                rank++;

                // Find peak months
                var monthCounts = data.Months.GroupBy(m => m)
                    .OrderByDescending(g => g.Count())
                    .Take(3)
                    .Select(g => g.Key);
                var peakMonths = string.Join(",", monthCounts);

                stats.Add(new RegionalIngredientStats
                {
                    GlobalIngredientId = data.GlobalIngredientId,
                    Region = region,
                    TotalUsageCount = data.TotalUsageCount,
                    AverageQuantity = data.AverageQuantity,
                    CommonUnit = data.CommonUnit,
                    PopularityRank = rank,
                    PenetrationRate = totalRecipesInRegion > 0
                        ? (decimal)data.UniqueRecipeCount / totalRecipesInRegion
                        : 0,
                    SeasonalPeakMonths = peakMonths,
                    LastUpdated = DateTime.UtcNow
                });
            }

            // Remove existing stats for this region
            await _db.RegionalIngredientStats
                .Where(s => s.Region == region)
                .ExecuteDeleteAsync(ct);

            // Add new stats
            await _db.RegionalIngredientStats.AddRangeAsync(stats, ct);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Computed {Count} regional stats for {Region}", stats.Count, region);
        }

        public async Task UpdatePairingsForRecipeAsync(
            Guid recipeId,
            IEnumerable<Guid> ingredientIds,
            CancellationToken ct = default)
        {
            var ids = ingredientIds.ToList();
            if (ids.Count < 2) return;

            // Generate all pairs
            for (int i = 0; i < ids.Count; i++)
            {
                for (int j = i + 1; j < ids.Count; j++)
                {
                    var idA = ids[i];
                    var idB = ids[j];

                    // Ensure consistent ordering
                    if (idA.CompareTo(idB) > 0)
                        (idA, idB) = (idB, idA);

                    var existing = await _db.IngredientPairings
                        .FirstOrDefaultAsync(p => p.IngredientAId == idA && p.IngredientBId == idB, ct);

                    if (existing != null)
                    {
                        existing.CoOccurrenceCount++;
                        existing.LastUpdated = DateTime.UtcNow;
                    }
                    else
                    {
                        _db.IngredientPairings.Add(new IngredientPairing
                        {
                            IngredientAId = idA,
                            IngredientBId = idB,
                            CoOccurrenceCount = 1,
                            AffinityScore = 0.5m, // Will be computed later
                            LastUpdated = DateTime.UtcNow
                        });
                    }
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        private static (string? region, string? countryCode, string? city) GetGeoFromIp(string? ipAddress)
        {
            // Simplified geo detection - in production, use a service like MaxMind GeoIP2
            // For now, return null and let the caller provide region if known
            if (string.IsNullOrEmpty(ipAddress))
                return (null, null, null);

            // TODO: Integrate with a geo IP service
            // For now, return placeholder based on common patterns
            return (null, null, null);
        }

        private static decimal? ParseQuantity(string? quantity)
        {
            if (string.IsNullOrWhiteSpace(quantity)) return null;

            // Extract numeric portion
            var numericPart = new string(quantity.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '/' || c == ' ').ToArray()).Trim();

            if (string.IsNullOrEmpty(numericPart)) return null;

            // Handle fractions like "1/2"
            if (numericPart.Contains('/'))
            {
                var parts = numericPart.Split('/');
                if (parts.Length == 2 &&
                    decimal.TryParse(parts[0].Trim(), out var numerator) &&
                    decimal.TryParse(parts[1].Trim(), out var denominator) &&
                    denominator != 0)
                {
                    return numerator / denominator;
                }
            }

            // Handle mixed numbers like "1 1/2"
            if (numericPart.Contains(' ') && numericPart.Contains('/'))
            {
                var spaceParts = numericPart.Split(' ');
                if (spaceParts.Length == 2 && decimal.TryParse(spaceParts[0], out var whole))
                {
                    var fractionParts = spaceParts[1].Split('/');
                    if (fractionParts.Length == 2 &&
                        decimal.TryParse(fractionParts[0], out var num) &&
                        decimal.TryParse(fractionParts[1], out var den) && den != 0)
                    {
                        return whole + (num / den);
                    }
                }
            }

            return decimal.TryParse(numericPart, out var result) ? result : null;
        }

        private static string? ExtractUnit(string? quantity)
        {
            if (string.IsNullOrWhiteSpace(quantity)) return null;

            // Skip numeric/fraction part and get the rest
            var unitPart = new string(quantity.SkipWhile(c => char.IsDigit(c) || c == '.' || c == '/' || c == ' ').ToArray()).Trim();

            return string.IsNullOrEmpty(unitPart) ? null : unitPart.ToLowerInvariant();
        }
    }
}
