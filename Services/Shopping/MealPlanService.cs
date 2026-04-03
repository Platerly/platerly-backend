using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Exceptions;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Shopping
{
    /// <summary>
    /// B2C user logic layer — manages meal plans (create, read, update, delete, share) and
    /// enforces tier-based duration limits.
    /// Depends on <see cref="PlaterlyDbContext"/> and <see cref="IShoppingListService"/>.
    ///
    /// Tier limits:
    ///   Free users: meal plans up to 7 days.
    ///   Premium users: meal plans up to 31 days.
    ///   Generating a shopping list from a meal plan is a Premium-only feature.
    ///
    /// Exceeding tier limits throws <c>PremiumRequiredException</c> (HTTP 402).
    /// </summary>
    public class MealPlanService : IMealPlanService
    {
        private readonly PlaterlyDbContext _db;
        private readonly IShoppingListService _shoppingListService;
        private readonly ILogger<MealPlanService> _logger;

        /// <summary>Maximum plan duration in days for free-tier users.</summary>
        private const int FREE_MAX_PLAN_DAYS = 7;

        /// <summary>Maximum plan duration in days for premium users.</summary>
        private const int PREMIUM_MAX_PLAN_DAYS = 31;

        public MealPlanService(
            PlaterlyDbContext db,
            IShoppingListService shoppingListService,
            ILogger<MealPlanService> logger)
        {
            _db = db;
            _shoppingListService = shoppingListService;
            _logger = logger;
        }

        private async Task<bool> IsUserPremiumAsync(Guid userId, CancellationToken ct = default)
        {
            return await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.IsPremium)
                .FirstOrDefaultAsync(ct);
        }

        /// <summary>Returns all non-deleted meal plans for the given user, ordered newest-first.</summary>
        /// <param name="userId">The authenticated user's ID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>List of <see cref="MealPlanSummaryDto"/> including total entry count.</returns>
        public async Task<List<MealPlanSummaryDto>> GetUserMealPlansAsync(Guid userId, CancellationToken ct = default)
        {
            return await _db.MealPlans
                .Where(p => p.UserId == userId && !p.IsDeleted)
                .OrderByDescending(p => p.StartDate)
                .Select(p => new MealPlanSummaryDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
                    TotalMeals = p.Entries.Count,
                    CreatedAt = p.CreatedAt
                })
                .AsNoTracking()
                .ToListAsync(ct);
        }

        /// <summary>
        /// Returns a single meal plan by ID including all entries. Returns <c>null</c> if not found,
        /// deleted, or if the plan is not shared and the requesting user is not the owner.
        /// </summary>
        /// <param name="planId">The meal plan ID to retrieve.</param>
        /// <param name="userId">Optional: the requesting user's ID for ownership/sharing validation.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The <see cref="MealPlanDto"/>, or <c>null</c> if inaccessible.</returns>
        public async Task<MealPlanDto?> GetMealPlanAsync(Guid planId, Guid? userId = null, CancellationToken ct = default)
        {
            var plan = await _db.MealPlans
                .Include(p => p.Entries.OrderBy(e => e.Date).ThenBy(e => e.MealType))
                    .ThenInclude(e => e.Recipe)
                .Where(p => p.Id == planId && !p.IsDeleted)
                .FirstOrDefaultAsync(ct);

            if (plan == null) return null;

            // Check access
            if (!plan.IsShared && userId.HasValue && plan.UserId != userId.Value)
                return null;

            return MapToDto(plan);
        }

        /// <summary>
        /// Creates a new meal plan for the user. Enforces the tier-based day-count limit:
        /// free users are capped at 7 days; premium at 31 days.
        /// </summary>
        /// <param name="userId">The authenticated user's ID.</param>
        /// <param name="dto">Plan details including name, start/end dates, and optional notes.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The created <see cref="MealPlanDto"/>.</returns>
        /// <exception cref="PremiumRequiredException">Thrown when plan duration exceeds the tier limit.</exception>
        public async Task<MealPlanDto> CreateMealPlanAsync(Guid userId, CreateMealPlanDto dto, CancellationToken ct = default)
        {
            var isPremium = await IsUserPremiumAsync(userId, ct);
            var planDays = (dto.EndDate - dto.StartDate).Days + 1;
            var maxDays = isPremium ? PREMIUM_MAX_PLAN_DAYS : FREE_MAX_PLAN_DAYS;

            if (planDays > maxDays)
            {
                throw new PremiumRequiredException(
                    isPremium
                        ? $"Meal plans cannot exceed {PREMIUM_MAX_PLAN_DAYS} days."
                        : $"Free users can only create meal plans up to {FREE_MAX_PLAN_DAYS} days. Upgrade to Premium for plans up to {PREMIUM_MAX_PLAN_DAYS} days.",
                    "meal_plan_days_limit");
            }

            var plan = new MealPlan
            {
                UserId = userId,
                Name = dto.Name,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _db.MealPlans.Add(plan);
            await _db.SaveChangesAsync(ct);

            return MapToDto(plan);
        }

        public async Task<MealPlanDto?> UpdateMealPlanAsync(Guid planId, Guid userId, CreateMealPlanDto dto, CancellationToken ct = default)
        {
            var plan = await _db.MealPlans
                .FirstOrDefaultAsync(p => p.Id == planId && p.UserId == userId && !p.IsDeleted, ct);

            if (plan == null) return null;

            // Check premium limits for plan duration
            var isPremium = await IsUserPremiumAsync(userId, ct);
            var planDays = (dto.EndDate - dto.StartDate).Days + 1;
            var maxDays = isPremium ? PREMIUM_MAX_PLAN_DAYS : FREE_MAX_PLAN_DAYS;

            if (planDays > maxDays)
            {
                throw new PremiumRequiredException(
                    isPremium
                        ? $"Meal plans cannot exceed {PREMIUM_MAX_PLAN_DAYS} days."
                        : $"Free users can only create meal plans up to {FREE_MAX_PLAN_DAYS} days. Upgrade to Premium for plans up to {PREMIUM_MAX_PLAN_DAYS} days.",
                    "meal_plan_days_limit");
            }

            plan.Name = dto.Name;
            plan.StartDate = dto.StartDate;
            plan.EndDate = dto.EndDate;
            plan.Notes = dto.Notes;
            plan.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return await GetMealPlanAsync(planId, userId, ct);
        }

        public async Task<bool> DeleteMealPlanAsync(Guid planId, Guid userId, CancellationToken ct = default)
        {
            var plan = await _db.MealPlans
                .FirstOrDefaultAsync(p => p.Id == planId && p.UserId == userId && !p.IsDeleted, ct);

            if (plan == null) return false;

            plan.IsDeleted = true;
            plan.DeletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<MealPlanEntryDto?> AddEntryAsync(Guid planId, Guid userId, AddMealPlanEntryDto dto, CancellationToken ct = default)
        {
            var plan = await _db.MealPlans
                .FirstOrDefaultAsync(p => p.Id == planId && p.UserId == userId && !p.IsDeleted, ct);

            if (plan == null) return null;

            // Verify recipe exists
            var recipe = await _db.Recipes
                .Where(r => r.Id == dto.RecipeId)
                .Select(r => new { r.Id, r.Title, r.ImageUrl })
                .FirstOrDefaultAsync(ct);

            if (recipe == null) return null;

            var entry = new MealPlanEntry
            {
                MealPlanId = planId,
                RecipeId = dto.RecipeId,
                Date = dto.Date,
                MealType = Enum.TryParse<MealType>(dto.MealType, true, out var mt) ? mt : MealType.Dinner,
                ScaleFactor = dto.ScaleFactor,
                Servings = dto.Servings,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _db.MealPlanEntries.Add(entry);
            plan.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new MealPlanEntryDto
            {
                Id = entry.Id,
                RecipeId = entry.RecipeId,
                RecipeTitle = recipe.Title,
                RecipeThumbnail = recipe.ImageUrl,
                Date = entry.Date,
                MealType = entry.MealType.ToString(),
                ScaleFactor = entry.ScaleFactor,
                Servings = entry.Servings,
                Notes = entry.Notes
            };
        }

        public async Task<MealPlanEntryDto?> UpdateEntryAsync(Guid planId, Guid entryId, Guid userId, AddMealPlanEntryDto dto, CancellationToken ct = default)
        {
            var entry = await _db.MealPlanEntries
                .Include(e => e.MealPlan)
                .Include(e => e.Recipe)
                .FirstOrDefaultAsync(e => e.Id == entryId && e.MealPlanId == planId, ct);

            if (entry?.MealPlan == null || entry.MealPlan.UserId != userId) return null;

            entry.RecipeId = dto.RecipeId;
            entry.Date = dto.Date;
            entry.MealType = Enum.TryParse<MealType>(dto.MealType, true, out var mt) ? mt : MealType.Dinner;
            entry.ScaleFactor = dto.ScaleFactor;
            entry.Servings = dto.Servings;
            entry.Notes = dto.Notes;
            entry.UpdatedAt = DateTime.UtcNow;
            entry.MealPlan.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            // Reload recipe info
            var recipe = await _db.Recipes
                .Where(r => r.Id == dto.RecipeId)
                .Select(r => new { r.Title, r.ImageUrl })
                .FirstOrDefaultAsync(ct);

            return new MealPlanEntryDto
            {
                Id = entry.Id,
                RecipeId = entry.RecipeId,
                RecipeTitle = recipe?.Title ?? "",
                RecipeThumbnail = recipe?.ImageUrl,
                Date = entry.Date,
                MealType = entry.MealType.ToString(),
                ScaleFactor = entry.ScaleFactor,
                Servings = entry.Servings,
                Notes = entry.Notes
            };
        }

        public async Task<bool> DeleteEntryAsync(Guid planId, Guid entryId, Guid userId, CancellationToken ct = default)
        {
            var entry = await _db.MealPlanEntries
                .Include(e => e.MealPlan)
                .FirstOrDefaultAsync(e => e.Id == entryId && e.MealPlanId == planId, ct);

            if (entry?.MealPlan == null || entry.MealPlan.UserId != userId) return false;

            _db.MealPlanEntries.Remove(entry);
            entry.MealPlan.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>
        /// Generates a shopping list from all recipes in the meal plan. Premium-only feature.
        /// Creates a new shopping list, adds ingredients from every meal plan entry
        /// (respecting scale factors and servings), and links the list to the meal plan.
        /// </summary>
        /// <param name="planId">The source meal plan.</param>
        /// <param name="userId">The authenticated user's ID (must be the plan owner and premium).</param>
        /// <param name="dto">Optional list name override.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The generated <see cref="ShoppingListDto"/>.</returns>
        /// <exception cref="PremiumRequiredException">Thrown for non-premium users.</exception>
        public async Task<ShoppingListDto> GenerateShoppingListAsync(Guid planId, Guid userId, GenerateShoppingListFromMealPlanDto dto, CancellationToken ct = default)
        {
            // Premium-only feature
            var isPremium = await IsUserPremiumAsync(userId, ct);
            if (!isPremium)
            {
                throw new PremiumRequiredException(
                    "Generating shopping lists from meal plans is a Premium feature. Upgrade to unlock this functionality.",
                    "meal_plan_shopping_list");
            }

            var plan = await _db.MealPlans
                .Include(p => p.Entries)
                .FirstOrDefaultAsync(p => p.Id == planId && p.UserId == userId && !p.IsDeleted, ct);

            if (plan == null)
                throw new InvalidOperationException("Meal plan not found.");

            // Create shopping list
            var listName = dto.ListName ?? $"Shopping for {plan.Name}";
            var createDto = new CreateShoppingListDto { Name = listName };
            var shoppingList = await _shoppingListService.CreateListAsync(userId, createDto, ct);

            // Add all recipes from the meal plan
            foreach (var entry in plan.Entries)
            {
                var recipeDto = new AddRecipeToListDto
                {
                    RecipeId = entry.RecipeId,
                    ScaleFactor = entry.ScaleFactor,
                    Servings = entry.Servings
                };
                await _shoppingListService.AddRecipeToListAsync(shoppingList.Id, userId, recipeDto, ct);
            }

            // Link the shopping list to the meal plan
            var list = await _db.ShoppingLists.FindAsync(new object[] { shoppingList.Id }, ct);
            if (list != null)
            {
                list.MealPlanId = planId;
                await _db.SaveChangesAsync(ct);
            }

            // Return the updated list
            return (await _shoppingListService.GetListAsync(shoppingList.Id, userId, ct))!;
        }

        private static MealPlanDto MapToDto(MealPlan plan)
        {
            return new MealPlanDto
            {
                Id = plan.Id,
                Name = plan.Name,
                StartDate = plan.StartDate,
                EndDate = plan.EndDate,
                Notes = plan.Notes,
                IsShared = plan.IsShared,
                ShareUrl = plan.IsShared && plan.ShareToken != null
                    ? $"/meal-plan/shared/{plan.ShareToken}"
                    : null,
                Entries = plan.Entries?.Select(e => new MealPlanEntryDto
                {
                    Id = e.Id,
                    RecipeId = e.RecipeId,
                    RecipeTitle = e.Recipe?.Title ?? "",
                    RecipeThumbnail = e.Recipe?.ImageUrl,
                    Date = e.Date,
                    MealType = e.MealType.ToString(),
                    ScaleFactor = e.ScaleFactor,
                    Servings = e.Servings,
                    Notes = e.Notes
                }).ToList() ?? new List<MealPlanEntryDto>(),
                CreatedAt = plan.CreatedAt,
                UpdatedAt = plan.UpdatedAt
            };
        }
    }
}
