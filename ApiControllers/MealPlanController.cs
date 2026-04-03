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
    /// <summary>
    /// B2C controller for creating and managing the authenticated user's meal plans.
    /// Supports full CRUD on meal plans, per-day meal entries, recipe search from cookbooks,
    /// and one-click shopping list generation from a plan.
    /// Creating/updating plans or generating shopping lists may return <c>402 Payment Required</c>
    /// when a free-tier limit is reached.
    /// Base route: <c>api/meal-plan</c>.
    /// All endpoints require a valid JWT (<c>[Authorize]</c> on the class).
    /// </summary>
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
        /// Retrieve all meal plans owned by the authenticated user, ordered by the service layer.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — array of meal plan summaries.<br/>
        /// <c>401</c> — unauthenticated.
        /// </returns>
        [HttpGet]
        public async Task<IActionResult> GetMyMealPlans(CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var plans = await _mealPlanService.GetUserMealPlansAsync(userId.Value, ct);
            return Ok(plans);
        }

        /// <summary>
        /// Retrieve a single meal plan, including all its entries.
        /// Only the owning user can access their plan.
        /// </summary>
        /// <param name="planId">The meal plan GUID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — full meal plan DTO with entries.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — plan not found or does not belong to this user.
        /// </returns>
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
        /// Create a new meal plan for the authenticated user. Start date must be on or before
        /// end date. Free-tier users may be subject to plan count limits enforced by the service.
        /// </summary>
        /// <param name="dto">Plan metadata: name, startDate, endDate.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>201 Created</c> — the created meal plan DTO; Location header points to
        /// <c>GET api/meal-plan/{planId}</c>.<br/>
        /// <c>400</c> — end date before start date.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>402</c> — free-tier plan limit reached
        /// (<c>{ error: "premium_required", code, message }</c>).
        /// </returns>
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
        /// Update an existing meal plan's name and/or date range.
        /// Only the owning user can modify their plan.
        /// </summary>
        /// <param name="planId">The meal plan GUID to update.</param>
        /// <param name="dto">Updated plan metadata (same shape as create).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — updated meal plan DTO.<br/>
        /// <c>400</c> — end date before start date.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>402</c> — premium limit.<br/>
        /// <c>404</c> — plan not found.
        /// </returns>
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
        /// Soft-delete a meal plan (marks it as deleted in the service; entries are removed).
        /// Only the owning user can delete their plan.
        /// </summary>
        /// <param name="planId">The meal plan GUID to delete.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — plan not found.
        /// </returns>
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
        /// Add a meal entry (recipe + date + meal slot) to the specified meal plan.
        /// </summary>
        /// <param name="planId">The meal plan GUID.</param>
        /// <param name="dto">Entry details: recipeId, date, mealType (Breakfast/Lunch/Dinner/Snack), servings.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — the created entry DTO.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — plan or recipe not found.
        /// </returns>
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
        /// Update an existing meal plan entry (e.g., change date, meal slot, or servings).
        /// </summary>
        /// <param name="planId">The parent meal plan GUID.</param>
        /// <param name="entryId">The entry GUID to update.</param>
        /// <param name="dto">Updated entry fields.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — updated entry DTO.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — entry not found.
        /// </returns>
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
        /// Remove a meal entry from the plan.
        /// </summary>
        /// <param name="planId">The parent meal plan GUID.</param>
        /// <param name="entryId">The entry GUID to delete.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>204 No Content</c> on success.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>404</c> — entry not found.
        /// </returns>
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
        /// Search recipes across all cookbooks owned by the authenticated user.
        /// Used by the meal plan entry picker so the user can find recipes to schedule.
        /// Matches against recipe title and description (case-insensitive).
        /// </summary>
        /// <param name="q">Search term (required).</param>
        /// <param name="limit">Maximum results to return, clamped 1–50 (default 10).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>{ recipes: [{ id, title, description, imageUrl, cookbookName,
        /// authorUsername }] }</c>. Returns empty array if query is blank.
        /// </returns>
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
        /// Generate a new shopping list by aggregating all ingredients from the specified meal plan.
        /// The list name, optional note, and scale factor can be provided in the request body.
        /// Premium limits apply; a <c>402</c> is returned if the free-tier shopping list cap is hit.
        /// </summary>
        /// <param name="planId">The source meal plan GUID.</param>
        /// <param name="dto">Options for the generated list (name, scaleFactor, etc.).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>200 OK</c> — the generated shopping list DTO.<br/>
        /// <c>401</c> — unauthenticated.<br/>
        /// <c>402</c> — premium limit reached.<br/>
        /// <c>404</c> — meal plan not found.
        /// </returns>
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
