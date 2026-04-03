using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>
    /// B2B data layer — shopping list behaviour analytics for food-retail and supply-chain partners.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Analyses shopping list items to identify top ingredients by list appearances and
    /// check-off rates. Also provides meal-planning pattern data (lists generated from meal plans,
    /// plan duration distribution, recipes per plan).
    /// </summary>
    public class ShoppingAnalyticsService : IShoppingAnalyticsService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<ShoppingAnalyticsService> _logger;

        public ShoppingAnalyticsService(
            PlaterlyDbContext db,
            ILogger<ShoppingAnalyticsService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Returns platform-wide shopping patterns: top ingredients by list appearances,
        /// check-off rates, and aggregate list statistics.
        /// </summary>
        /// <param name="take">Maximum number of ingredient patterns to return.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A <see cref="ShoppingPatternsResponse"/> with ingredient and list-level statistics.</returns>
        public async Task<ShoppingPatternsResponse> GetPatternsAsync(
            int take = 50,
            CancellationToken ct = default)
        {
            var totalLists = await _db.ShoppingLists
                .Where(sl => !sl.IsDeleted)
                .CountAsync(ct);

            // Top ingredients by list appearances
            var topIngredients = await _db.ShoppingListItems
                .Include(si => si.ShoppingList)
                .Include(si => si.GlobalIngredient)
                .Where(si => si.ShoppingList != null && !si.ShoppingList.IsDeleted && si.GlobalIngredient != null)
                .GroupBy(si => new { si.GlobalIngredientId, Name = si.GlobalIngredient!.Name })
                .Select(g => new ShoppingPatternDto
                {
                    IngredientName = g.Key.Name,
                    ListAppearances = g.Count(),
                    CheckedOffRate = g.Count() > 0
                        ? Math.Round((decimal)g.Count(i => i.IsChecked) / g.Count() * 100, 1)
                        : 0,
                    AverageQuantity = g.Where(i => i.NumericAmount.HasValue)
                        .Average(i => (decimal?)i.NumericAmount) ?? 0,
                    CommonUnit = g.Where(i => i.Unit != null)
                        .GroupBy(i => i.Unit)
                        .OrderByDescending(ug => ug.Count())
                        .Select(ug => ug.Key)
                        .FirstOrDefault()
                })
                .OrderByDescending(x => x.ListAppearances)
                .Take(take)
                .ToListAsync(ct);

            // Average list size
            var avgListSize = totalLists > 0
                ? await _db.ShoppingListItems
                    .Include(si => si.ShoppingList)
                    .Where(si => si.ShoppingList != null && !si.ShoppingList.IsDeleted)
                    .GroupBy(si => si.ShoppingListId)
                    .AverageAsync(g => (decimal)g.Count(), ct)
                : 0;

            // Average checked-off rate
            var totalItems = await _db.ShoppingListItems
                .Include(si => si.ShoppingList)
                .Where(si => si.ShoppingList != null && !si.ShoppingList.IsDeleted)
                .CountAsync(ct);
            var checkedItems = await _db.ShoppingListItems
                .Include(si => si.ShoppingList)
                .Where(si => si.ShoppingList != null && !si.ShoppingList.IsDeleted && si.IsChecked)
                .CountAsync(ct);
            var avgCheckedRate = totalItems > 0
                ? Math.Round((decimal)checkedItems / totalItems * 100, 1)
                : 0;

            // Category distribution
            var categoryDist = await _db.ShoppingListItems
                .Include(si => si.ShoppingList)
                .Where(si => si.ShoppingList != null && !si.ShoppingList.IsDeleted && si.Category != null)
                .GroupBy(si => si.Category!)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(20)
                .ToListAsync(ct);

            return new ShoppingPatternsResponse
            {
                TopIngredients = topIngredients,
                AverageListSize = Math.Round(avgListSize, 1),
                AverageCheckedOffRate = avgCheckedRate,
                TotalListsAnalyzed = totalLists,
                CategoryDistribution = categoryDist.ToDictionary(x => x.Category, x => x.Count)
            };
        }

        public async Task<MealPlanningPatternsResponse> GetMealPlanningPatternsAsync(
            int take = 20,
            CancellationToken ct = default)
        {
            var totalPlans = await _db.MealPlans
                .Where(mp => !mp.IsDeleted)
                .CountAsync(ct);

            // Meal type distribution
            var mealTypeDist = await _db.MealPlanEntries
                .Include(e => e.MealPlan)
                .Where(e => e.MealPlan != null && !e.MealPlan.IsDeleted)
                .GroupBy(e => e.MealType)
                .Select(g => new { MealType = g.Key.ToString(), Count = g.Count() })
                .ToListAsync(ct);

            // Average plan duration
            var avgDuration = totalPlans > 0
                ? await _db.MealPlans
                    .Where(mp => !mp.IsDeleted)
                    .AverageAsync(mp => EF.Functions.DateDiffDay(mp.StartDate, mp.EndDate), ct)
                : 0;

            // Average recipes per plan
            var avgRecipesPerPlan = totalPlans > 0
                ? await _db.MealPlanEntries
                    .Include(e => e.MealPlan)
                    .Where(e => e.MealPlan != null && !e.MealPlan.IsDeleted)
                    .GroupBy(e => e.MealPlanId)
                    .AverageAsync(g => (decimal)g.Count(), ct)
                : 0;

            // Top recipes in meal plans
            var topRecipes = await _db.MealPlanEntries
                .Include(e => e.Recipe)
                .Include(e => e.MealPlan)
                .Where(e => e.Recipe != null && !e.Recipe.IsDeleted &&
                       e.MealPlan != null && !e.MealPlan.IsDeleted)
                .GroupBy(e => new { e.RecipeId, e.Recipe!.Title, e.Recipe.Region })
                .Select(g => new PopularMealPlanRecipeDto
                {
                    RecipeId = g.Key.RecipeId,
                    RecipeTitle = g.Key.Title,
                    Region = g.Key.Region,
                    PlanAppearances = g.Count(),
                    MostCommonMealType = g.GroupBy(x => x.MealType)
                        .OrderByDescending(mg => mg.Count())
                        .Select(mg => mg.Key.ToString())
                        .FirstOrDefault() ?? "Dinner"
                })
                .OrderByDescending(x => x.PlanAppearances)
                .Take(take)
                .ToListAsync(ct);

            return new MealPlanningPatternsResponse
            {
                MealTypeDistribution = mealTypeDist.ToDictionary(x => x.MealType, x => x.Count),
                AveragePlanDurationDays = Math.Round((decimal)avgDuration, 1),
                AverageRecipesPerPlan = Math.Round(avgRecipesPerPlan, 1),
                TotalPlansAnalyzed = totalPlans,
                TopRecipes = topRecipes
            };
        }
    }
}
