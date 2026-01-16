using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Exceptions;
using Platerly.Api.Extensions;
using Platerly.Api.Services.Shopping;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/meal-plan")]
    [Authorize]
    public class MealPlanController : ControllerBase
    {
        private readonly IMealPlanService _mealPlanService;
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<MealPlanController> _logger;

        public MealPlanController(
            IMealPlanService mealPlanService,
            PlaterlyDbContext db,
            ILogger<MealPlanController> logger)
        {
            _mealPlanService = mealPlanService;
            _db = db;
            _logger = logger;
        }

        private Guid? CurrentUserId() => User.GetUserIdOrNull();

        // ========== Meal Plan CRUD ==========

        /// <summary>
        /// Get all meal plans for the current user
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetMyMealPlans(CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var plans = await _mealPlanService.GetUserMealPlansAsync(userId.Value, ct);
            return Ok(plans);
        }

        /// <summary>
        /// Get a specific meal plan by ID
        /// </summary>
        [HttpGet("{planId:guid}")]
        public async Task<IActionResult> GetMealPlan(Guid planId, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var plan = await _mealPlanService.GetMealPlanAsync(planId, userId.Value, ct);
            if (plan == null) return NotFound("Meal plan not found.");

            return Ok(plan);
        }

        /// <summary>
        /// Create a new meal plan
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateMealPlan([FromBody] CreateMealPlanDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            if (dto.EndDate < dto.StartDate)
                return BadRequest("End date must be after start date.");

            try
            {
                var plan = await _mealPlanService.CreateMealPlanAsync(userId.Value, dto, ct);
                return CreatedAtAction(nameof(GetMealPlan), new { planId = plan.Id }, plan);
            }
            catch (PremiumRequiredException ex)
            {
                return StatusCode(402, new { error = "premium_required", code = ex.LimitCode, message = ex.Message });
            }
        }

        /// <summary>
        /// Update a meal plan
        /// </summary>
        [HttpPut("{planId:guid}")]
        public async Task<IActionResult> UpdateMealPlan(Guid planId, [FromBody] CreateMealPlanDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            if (dto.EndDate < dto.StartDate)
                return BadRequest("End date must be after start date.");

            try
            {
                var plan = await _mealPlanService.UpdateMealPlanAsync(planId, userId.Value, dto, ct);
                if (plan == null) return NotFound("Meal plan not found.");

                return Ok(plan);
            }
            catch (PremiumRequiredException ex)
            {
                return StatusCode(402, new { error = "premium_required", code = ex.LimitCode, message = ex.Message });
            }
        }

        /// <summary>
        /// Delete a meal plan (soft delete)
        /// </summary>
        [HttpDelete("{planId:guid}")]
        public async Task<IActionResult> DeleteMealPlan(Guid planId, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var deleted = await _mealPlanService.DeleteMealPlanAsync(planId, userId.Value, ct);
            if (!deleted) return NotFound("Meal plan not found.");

            return NoContent();
        }

        // ========== Entry Management ==========

        /// <summary>
        /// Add a meal entry to the plan
        /// </summary>
        [HttpPost("{planId:guid}/entries")]
        public async Task<IActionResult> AddEntry(Guid planId, [FromBody] AddMealPlanEntryDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var entry = await _mealPlanService.AddEntryAsync(planId, userId.Value, dto, ct);
            if (entry == null) return NotFound("Meal plan or recipe not found.");

            return Ok(entry);
        }

        /// <summary>
        /// Update a meal entry
        /// </summary>
        [HttpPut("{planId:guid}/entries/{entryId:guid}")]
        public async Task<IActionResult> UpdateEntry(Guid planId, Guid entryId, [FromBody] AddMealPlanEntryDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var entry = await _mealPlanService.UpdateEntryAsync(planId, entryId, userId.Value, dto, ct);
            if (entry == null) return NotFound("Entry not found.");

            return Ok(entry);
        }

        /// <summary>
        /// Delete a meal entry
        /// </summary>
        [HttpDelete("{planId:guid}/entries/{entryId:guid}")]
        public async Task<IActionResult> DeleteEntry(Guid planId, Guid entryId, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var deleted = await _mealPlanService.DeleteEntryAsync(planId, entryId, userId.Value, ct);
            if (!deleted) return NotFound("Entry not found.");

            return NoContent();
        }

        // ========== Recipe Search (from user's cookbooks) ==========

        /// <summary>
        /// Search recipes from user's cookbooks for meal planning
        /// </summary>
        [HttpGet("recipes/search")]
        public async Task<IActionResult> SearchMyRecipes([FromQuery] string q, [FromQuery] int limit = 10, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            if (string.IsNullOrWhiteSpace(q))
                return Ok(new { recipes = Array.Empty<object>() });

            var searchTerm = q.ToLower().Trim();
            limit = Math.Clamp(limit, 1, 50);

            // Get all recipes from cookbooks owned by this user
            var recipes = await _db.CookbookRecipes
                .Include(cr => cr.Cookbook)
                .Include(cr => cr.Recipe)
                .Where(cr => cr.Cookbook.UserId == userId.Value)
                .Where(cr => cr.Recipe.Title.ToLower().Contains(searchTerm)
                          || cr.Recipe.Description.ToLower().Contains(searchTerm))
                .Select(cr => new
                {
                    id = cr.Recipe.Id,
                    title = cr.Recipe.Title,
                    description = cr.Recipe.Description,
                    imageUrl = cr.Recipe.ImageUrl,
                    cookbookName = cr.Cookbook.Title,
                    authorUsername = cr.Recipe.User != null ? cr.Recipe.User.Username : null
                })
                .Distinct()
                .Take(limit)
                .AsNoTracking()
                .ToListAsync(ct);

            return Ok(new { recipes });
        }

        // ========== Generate Shopping List ==========

        /// <summary>
        /// Generate a shopping list from this meal plan
        /// </summary>
        [HttpPost("{planId:guid}/shopping-list")]
        public async Task<IActionResult> GenerateShoppingList(Guid planId, [FromBody] GenerateShoppingListFromMealPlanDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var list = await _mealPlanService.GenerateShoppingListAsync(planId, userId.Value, dto, ct);
                return Ok(list);
            }
            catch (PremiumRequiredException ex)
            {
                return StatusCode(402, new { error = "premium_required", code = ex.LimitCode, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }
    }
}
