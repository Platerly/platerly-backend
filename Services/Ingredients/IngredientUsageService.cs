using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    /// <summary>
    /// B2C and analytics layer — records ingredient usage events, ingredient search queries,
    /// co-occurrence pairings, and pre-computed trend/regional statistics.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Key behaviours:
    /// <list type="bullet">
    ///   <item><see cref="LogRecipeIngredientsAsync"/> writes a batch of <c>IngredientUsageLog</c>
    ///     rows, increments <c>GlobalIngredient.TotalUsageCount</c>, then fires
    ///     <see cref="UpdatePairingsForRecipeAsync"/> in the background (fire-and-forget, no await)
    ///     so pairing updates do not add latency to the caller.</item>
    ///   <item><see cref="RecomputeDailyTrendsAsync"/> deletes and rebuilds all
    ///     <c>IngredientTrendDaily</c> rows for a given date using day-over-day and week-over-week
    ///     change percentages; trend direction: +1 if DoD &gt; 5%, -1 if &lt; -5%.</item>
    ///   <item><see cref="RecomputeRegionalStatsAsync"/> rebuilds <c>RegionalIngredientStats</c>
    ///     for a given region, ranking ingredients by total usage count and computing
    ///     penetration rate as <c>uniqueRecipeCount / totalRecipesInRegion</c>.</item>
    ///   <item>IP-to-geo resolution is a stub (returns null); production integration point for
    ///     MaxMind GeoIP2 or equivalent.</item>
    /// </list>
    /// </summary>
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

        /// <summary>
        /// Records a <c>IngredientUsageLog</c> row for every ingredient in the recipe, increments
        /// each ingredient's <c>TotalUsageCount</c>, and fires <see cref="UpdatePairingsForRecipeAsync"/>
        /// in the background without awaiting it. Geographic metadata is derived from
        /// <paramref name="ipAddress"/> via a stub (returns null until a GeoIP service is wired up).
        /// </summary>
        /// <param name="recipeId">Recipe being cooked/viewed.</param>
        /// <param name="userId">User who triggered the usage event.</param>
        /// <param name="ingredients">Collection of recipe ingredients to log.</param>
        /// <param name="ipAddress">Optional client IP for geo-resolution (stub in current impl).</param>
        /// <param name="userAgent">Unused — reserved for future device analytics.</param>
        /// <param name="ct">Cancellation token.</param>
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

        /// <summary>
        /// Records a single <c>IngredientUsageLog</c> row for an individual ingredient, with
        /// caller-supplied geo and dietary-tag metadata. Does not increment
        /// <c>GlobalIngredient.TotalUsageCount</c> — use <see cref="LogRecipeIngredientsAsync"/>
        /// for batch logging that also updates counts and pairings.
        /// </summary>
        /// <param name="globalIngredientId">Global ingredient to log usage for.</param>
        /// <param name="recipeId">Recipe the ingredient appears in.</param>
        /// <param name="userId">User who triggered the event.</param>
        /// <param name="quantity">Raw quantity string (e.g., "1 1/2 cups").</param>
        /// <param name="unit">Extracted unit (e.g., "cups").</param>
        /// <param name="region">Geographic region code.</param>
        /// <param name="countryCode">ISO country code.</param>
        /// <param name="city">City name.</param>
        /// <param name="dietaryTags">Comma-separated dietary tag names from the recipe.</param>
        /// <param name="ct">Cancellation token.</param>
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

        /// <summary>
        /// Records an <c>IngredientSearchLog</c> row. Queries longer than 256 characters are
        /// truncated. <c>WasFound</c> is set to <c>true</c> when <paramref name="resultCount"/> &gt; 0.
        /// </summary>
        /// <param name="query">Raw user search query.</param>
        /// <param name="resultCount">Number of results returned for the query.</param>
        /// <param name="region">Optional geographic region of the searching user.</param>
        /// <param name="searchContext">Optional context label (e.g., "substitution", "recipe-form").</param>
        /// <param name="ct">Cancellation token.</param>
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

        /// <summary>
        /// Rebuilds all <c>IngredientTrendDaily</c> rows for <paramref name="date"/> from
        /// raw <c>IngredientUsageLog</c> data. Deletes existing rows for the date first, then
        /// inserts fresh computed rows. Day-over-day and week-over-week changes are expressed as
        /// percentage change from the prior period (0 when no prior data exists).
        /// TrendDirection: +1 when DoD &gt; +5%, -1 when DoD &lt; -5%, 0 otherwise.
        /// </summary>
        /// <param name="date">The date to (re)compute trends for.</param>
        /// <param name="ct">Cancellation token.</param>
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

        /// <summary>
        /// Rebuilds all <c>RegionalIngredientStats</c> rows for <paramref name="region"/> from
        /// raw <c>IngredientUsageLog</c> data. Deletes existing rows for the region first, then
        /// inserts fresh computed rows ranked by total usage count. Penetration rate is computed as
        /// <c>uniqueRecipeCount / totalRecipesInRegion</c>. Top-3 seasonal peak months are derived
        /// from the months of all usage-log timestamps.
        /// </summary>
        /// <param name="region">Region code to (re)compute stats for (e.g., "CA", "US").</param>
        /// <param name="ct">Cancellation token.</param>
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

        /// <summary>
        /// Updates <c>IngredientPairing</c> rows for every pair of ingredients in the recipe.
        /// Pairs are always stored with the lexicographically smaller ID as <c>IngredientAId</c>
        /// to ensure a canonical ordering and avoid duplicate rows. Increments
        /// <c>CoOccurrenceCount</c> on existing pairs; creates new rows with an initial
        /// <c>AffinityScore</c> of 0.5 (refined by a separate offline process).
        /// Called fire-and-forget from <see cref="LogRecipeIngredientsAsync"/> using
        /// <c>CancellationToken.None</c> so it runs to completion independently of the request.
        /// </summary>
        /// <param name="recipeId">Recipe that triggered the pairing update (unused in query, reserved).</param>
        /// <param name="ingredientIds">All global ingredient IDs present in the recipe.</param>
        /// <param name="ct">Cancellation token.</param>
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
            // GEO-IP STUB — always returns (null, null, null).
            //
            // IMPORTANT: Any B2B API responses that include regional breakdowns derived from
            // this method will return null/empty for all geo fields until this is implemented.
            // Consumers of the RegionalDemand, IngredientUsage, and ShoppingAnalytics endpoints
            // should be informed that geo attribution is not yet available.
            //
            // TODO: Integrate MaxMind GeoLite2 (free, MMDB format) or IP-API.com before exposing
            // regional breakdown data to B2B customers. Steps:
            //   1. Download GeoLite2-City.mmdb from maxmind.com (requires free account).
            //   2. Add NuGet package: MaxMind.GeoIP2 (or MaxMind.Db for the lighter reader).
            //   3. Register IDatabaseReader as a singleton in Program.cs.
            //   4. Replace this stub with a reader.City(ipAddress) call.
            //   5. Refresh the MMDB file monthly (MaxMind updates it weekly).
            // Alternatively, remove regional breakdown fields from B2B responses entirely until
            // the integration is complete to avoid misleading consumers with null data.
            if (string.IsNullOrEmpty(ipAddress))
                return (null, null, null);

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
