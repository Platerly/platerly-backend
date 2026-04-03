using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Ingredients
{
    /// <summary>
    /// B2C and B2B data layer — ingredient usage and trending analytics.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Provides trending ingredient data from the <c>IngredientTrendDaily</c> pre-computed table,
    /// top-used ingredients by recipe count, per-ingredient usage details, category statistics,
    /// allergen prevalence across ingredients, and regional usage breakdowns.
    /// </summary>
    public class IngredientAnalyticsService : IIngredientAnalyticsService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<IngredientAnalyticsService> _logger;

        public IngredientAnalyticsService(
            PlaterlyDbContext db,
            ILogger<IngredientAnalyticsService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Returns trending ingredients from the daily pre-computed trend table over a configurable day window.
        /// Supports optional region and category-slug filtering. Ordered by usage count, then day-over-day change.
        /// </summary>
        /// <param name="request">Day window, optional region, optional category slug, take count.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Trending ingredient trend rows with usage, change, and rank data.</returns>
        public async Task<TrendingIngredientsResponse> GetTrendingIngredientsAsync(
            TrendingIngredientsRequest request,
            CancellationToken ct = default)
        {
            var endDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var startDate = endDate.AddDays(-request.Days);

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

            var trends = await query
                .OrderByDescending(t => t.UsageCount)
                .ThenByDescending(t => t.DayOverDayChange)
                .Take(request.Take)
                .Select(t => new IngredientTrendDto
                {
                    GlobalIngredientId = t.GlobalIngredientId,
                    IngredientName = t.GlobalIngredient.Name,
                    Category = t.GlobalIngredient.Category != null ? t.GlobalIngredient.Category.Name : null,
                    Date = t.Date,
                    Region = t.Region,
                    UsageCount = t.UsageCount,
                    UniqueRecipes = t.UniqueRecipes,
                    UniqueUsers = t.UniqueUsers,
                    DayOverDayChange = t.DayOverDayChange,
                    WeekOverWeekChange = t.WeekOverWeekChange,
                    TrendDirection = t.TrendDirection,
                    PopularityRank = t.PopularityRank
                })
                .AsNoTracking()
                .ToListAsync(ct);

            return new TrendingIngredientsResponse
            {
                Trends = trends,
                Region = request.Region,
                StartDate = startDate,
                EndDate = endDate,
                TotalIngredients = trends.Select(t => t.GlobalIngredientId).Distinct().Count()
            };
        }

        public async Task<RegionalStatsResponse> GetRegionalStatsAsync(
            RegionalStatsRequest request,
            CancellationToken ct = default)
        {
            var query = _db.RegionalIngredientStats
                .Include(s => s.GlobalIngredient)
                    .ThenInclude(g => g.Category)
                .Where(s => s.Region == request.Region);

            if (!string.IsNullOrEmpty(request.Category))
            {
                query = query.Where(s => s.GlobalIngredient.Category != null &&
                    s.GlobalIngredient.Category.Slug == request.Category);
            }

            var stats = await query
                .OrderBy(s => s.PopularityRank)
                .Take(request.Take)
                .Select(s => new RegionalIngredientStatsDto
                {
                    GlobalIngredientId = s.GlobalIngredientId,
                    IngredientName = s.GlobalIngredient.Name,
                    Category = s.GlobalIngredient.Category != null ? s.GlobalIngredient.Category.Name : null,
                    Region = s.Region,
                    TotalUsageCount = s.TotalUsageCount,
                    PenetrationRate = s.PenetrationRate,
                    AverageQuantity = s.AverageQuantity,
                    CommonUnit = s.CommonUnit,
                    PopularityRank = s.PopularityRank,
                    SeasonalPeakMonths = s.SeasonalPeakMonths,
                    CommonPairings = s.CommonPairings
                })
                .AsNoTracking()
                .ToListAsync(ct);

            var totalUsages = await _db.RegionalIngredientStats
                .Where(s => s.Region == request.Region)
                .SumAsync(s => s.TotalUsageCount, ct);

            var lastUpdated = await _db.RegionalIngredientStats
                .Where(s => s.Region == request.Region)
                .MaxAsync(s => (DateTime?)s.LastUpdated, ct) ?? DateTime.UtcNow;

            return new RegionalStatsResponse
            {
                Region = request.Region,
                Stats = stats,
                TotalIngredientsInRegion = stats.Count,
                TotalUsagesInRegion = totalUsages,
                LastUpdated = lastUpdated
            };
        }

        public async Task<PairingsResponse> GetPairingsAsync(
            PairingsRequest request,
            CancellationToken ct = default)
        {
            var query = _db.IngredientPairings
                .Include(p => p.IngredientA)
                .Include(p => p.IngredientB)
                .AsQueryable();

            string? ingredientName = null;

            if (request.IngredientId.HasValue)
            {
                var ingredientId = request.IngredientId.Value;
                query = query.Where(p => p.IngredientAId == ingredientId || p.IngredientBId == ingredientId);

                ingredientName = await _db.GlobalIngredients
                    .Where(g => g.Id == ingredientId)
                    .Select(g => g.Name)
                    .FirstOrDefaultAsync(ct);
            }

            if (request.MinAffinityScore.HasValue)
            {
                query = query.Where(p => p.AffinityScore >= request.MinAffinityScore.Value);
            }

            var pairings = await query
                .OrderByDescending(p => p.CoOccurrenceCount)
                .ThenByDescending(p => p.AffinityScore)
                .Take(request.Take)
                .Select(p => new IngredientPairingDto
                {
                    IngredientAId = p.IngredientAId,
                    IngredientAName = p.IngredientA.Name,
                    IngredientBId = p.IngredientBId,
                    IngredientBName = p.IngredientB.Name,
                    CoOccurrenceCount = p.CoOccurrenceCount,
                    AffinityScore = p.AffinityScore,
                    TopRegion = p.TopRegion,
                    CommonCuisines = p.CommonCuisines
                })
                .AsNoTracking()
                .ToListAsync(ct);

            var totalCount = request.IngredientId.HasValue
                ? await _db.IngredientPairings
                    .Where(p => p.IngredientAId == request.IngredientId || p.IngredientBId == request.IngredientId)
                    .CountAsync(ct)
                : await _db.IngredientPairings.CountAsync(ct);

            return new PairingsResponse
            {
                ForIngredientId = request.IngredientId,
                ForIngredientName = ingredientName,
                Pairings = pairings,
                TotalPairings = totalCount
            };
        }

        public async Task<PopularIngredientsResponse> GetPopularIngredientsAsync(
            PopularIngredientsRequest request,
            CancellationToken ct = default)
        {
            var query = _db.GlobalIngredients
                .Include(g => g.Category)
                .Where(g => g.IsActive && g.TotalUsageCount > 0);

            if (!string.IsNullOrEmpty(request.Region))
            {
                query = query.Where(g => g.PrimaryRegion == request.Region);
            }

            if (!string.IsNullOrEmpty(request.Category))
            {
                query = query.Where(g => g.Category != null && g.Category.Slug == request.Category);
            }

            if (!string.IsNullOrEmpty(request.DietaryFilter))
            {
                query = query.Where(g => g.DietaryFlags != null &&
                    g.DietaryFlags.Contains(request.DietaryFilter));
            }

            var ingredients = await query
                .OrderByDescending(g => g.TotalUsageCount)
                .Take(request.Take)
                .Select(g => new PopularIngredientDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    Category = g.Category != null ? g.Category.Name : null,
                    TotalUsageCount = g.TotalUsageCount,
                    UniqueRecipeCount = g.UniqueRecipeCount,
                    PopularityRank = g.PopularityRank,
                    PrimaryRegion = g.PrimaryRegion,
                    DietaryFlags = g.DietaryFlags,
                    AverageRecipeRating = g.AverageRecipeRating
                })
                .AsNoTracking()
                .ToListAsync(ct);

            return new PopularIngredientsResponse
            {
                Ingredients = ingredients,
                Region = request.Region,
                Category = request.Category,
                TotalCount = ingredients.Count
            };
        }

        public async Task<SearchDemandResponse> GetSearchDemandAsync(
            SearchDemandRequest request,
            CancellationToken ct = default)
        {
            var endDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var startDate = endDate.AddDays(-request.Days);
            var startDateTime = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

            var query = _db.IngredientSearchLogs
                .Where(s => s.CreatedAt >= startDateTime && s.CreatedAt <= endDateTime);

            if (!string.IsNullOrEmpty(request.Region))
            {
                query = query.Where(s => s.Region == request.Region);
            }

            if (request.OnlyUnfound == true)
            {
                query = query.Where(s => !s.WasFound);
            }

            var searches = await query
                .GroupBy(s => new { s.Query, s.WasFound, s.Region, s.SearchContext })
                .Select(g => new SearchDemandDto
                {
                    Query = g.Key.Query,
                    SearchCount = g.Count(),
                    WasFound = g.Key.WasFound,
                    Region = g.Key.Region,
                    SearchContext = g.Key.SearchContext
                })
                .OrderByDescending(s => s.SearchCount)
                .Take(request.Take)
                .AsNoTracking()
                .ToListAsync(ct);

            var totalSearches = await _db.IngredientSearchLogs
                .Where(s => s.CreatedAt >= startDateTime && s.CreatedAt <= endDateTime)
                .CountAsync(ct);

            var unfoundSearches = await _db.IngredientSearchLogs
                .Where(s => s.CreatedAt >= startDateTime && s.CreatedAt <= endDateTime && !s.WasFound)
                .CountAsync(ct);

            return new SearchDemandResponse
            {
                Searches = searches,
                TotalSearches = totalSearches,
                UnfoundSearches = unfoundSearches,
                StartDate = startDate,
                EndDate = endDate
            };
        }

        public async Task<DietaryTrendsResponse> GetDietaryTrendsAsync(
            DietaryTrendsRequest request,
            CancellationToken ct = default)
        {
            var endDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var startDate = endDate.AddDays(-request.Days);

            var query = _db.DietaryTrends
                .Where(t => t.Date >= startDate && t.Date <= endDate);

            if (!string.IsNullOrEmpty(request.Region))
            {
                query = query.Where(t => t.Region == request.Region);
            }

            if (!string.IsNullOrEmpty(request.DietaryTag))
            {
                query = query.Where(t => t.DietaryTag == request.DietaryTag);
            }

            var trends = await query
                .OrderByDescending(t => t.RecipeCount)
                .Select(t => new DietaryTrendDto
                {
                    DietaryTag = t.DietaryTag,
                    Date = t.Date,
                    Region = t.Region,
                    RecipeCount = t.RecipeCount,
                    UniqueUsers = t.UniqueUsers,
                    GrowthRate = t.GrowthRate,
                    MarketShare = t.MarketShare
                })
                .AsNoTracking()
                .ToListAsync(ct);

            return new DietaryTrendsResponse
            {
                Trends = trends,
                Region = request.Region,
                StartDate = startDate,
                EndDate = endDate
            };
        }

        public async Task<CategoryAnalyticsResponse> GetCategoryAnalyticsAsync(
            CancellationToken ct = default)
        {
            var categories = await _db.IngredientCategories
                .Include(c => c.ParentCategory)
                .Include(c => c.Ingredients)
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .Select(c => new CategoryAnalyticsDto
                {
                    CategoryId = c.Id,
                    CategoryName = c.Name,
                    Slug = c.Slug,
                    ParentCategoryId = c.ParentCategoryId,
                    ParentCategoryName = c.ParentCategory != null ? c.ParentCategory.Name : null,
                    IngredientCount = c.Ingredients.Count(i => i.IsActive),
                    TotalUsageCount = c.Ingredients.Sum(i => i.TotalUsageCount),
                    UniqueRecipeCount = c.Ingredients.Sum(i => i.UniqueRecipeCount),
                    TopIngredients = string.Join(",", c.Ingredients
                        .Where(i => i.IsActive)
                        .OrderByDescending(i => i.TotalUsageCount)
                        .Take(5)
                        .Select(i => i.Name))
                })
                .AsNoTracking()
                .ToListAsync(ct);

            return new CategoryAnalyticsResponse
            {
                Categories = categories,
                TotalCategories = categories.Count
            };
        }

        public async Task<List<string>> GetAvailableRegionsAsync(CancellationToken ct = default)
        {
            var regions = await _db.IngredientUsageLogs
                .Where(l => l.Region != null)
                .Select(l => l.Region!)
                .Distinct()
                .OrderBy(r => r)
                .ToListAsync(ct);

            return regions;
        }
    }
}
