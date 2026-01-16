using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Exceptions;
using Platerly.Api.Extensions;
using Platerly.Api.Services.Shopping;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("api/shopping-list")]
    public class ShoppingListController : ControllerBase
    {
        private readonly IShoppingListService _shoppingListService;
        private readonly ILogger<ShoppingListController> _logger;

        public ShoppingListController(
            IShoppingListService shoppingListService,
            ILogger<ShoppingListController> logger)
        {
            _shoppingListService = shoppingListService;
            _logger = logger;
        }

        private Guid? CurrentUserId() => User.GetUserIdOrNull();

        // ========== List CRUD ==========

        /// <summary>
        /// Get all shopping lists for the current user
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetMyLists(CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var lists = await _shoppingListService.GetUserListsAsync(userId.Value, ct);
            return Ok(lists);
        }

        /// <summary>
        /// Get a specific shopping list by ID
        /// </summary>
        [Authorize]
        [HttpGet("{listId:guid}")]
        public async Task<IActionResult> GetList(Guid listId, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var list = await _shoppingListService.GetListAsync(listId, userId.Value, ct);
            if (list == null) return NotFound("Shopping list not found.");

            return Ok(list);
        }

        /// <summary>
        /// Get a shared shopping list by share token (public endpoint)
        /// </summary>
        [AllowAnonymous]
        [HttpGet("shared/{shareToken}")]
        public async Task<IActionResult> GetSharedList(string shareToken, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(shareToken))
                return BadRequest("Share token is required.");

            var list = await _shoppingListService.GetListByShareTokenAsync(shareToken, ct);
            if (list == null) return NotFound("Shared list not found or sharing is disabled.");

            return Ok(list);
        }

        /// <summary>
        /// Create a new shopping list
        /// </summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> CreateList([FromBody] CreateShoppingListDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var list = await _shoppingListService.CreateListAsync(userId.Value, dto, ct);
                return CreatedAtAction(nameof(GetList), new { listId = list.Id }, list);
            }
            catch (PremiumRequiredException ex)
            {
                return StatusCode(402, new { error = "premium_required", code = ex.LimitCode, message = ex.Message });
            }
        }

        /// <summary>
        /// Update a shopping list
        /// </summary>
        [Authorize]
        [HttpPut("{listId:guid}")]
        public async Task<IActionResult> UpdateList(Guid listId, [FromBody] UpdateShoppingListDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var list = await _shoppingListService.UpdateListAsync(listId, userId.Value, dto, ct);
            if (list == null) return NotFound("Shopping list not found.");

            return Ok(list);
        }

        /// <summary>
        /// Delete a shopping list (soft delete)
        /// </summary>
        [Authorize]
        [HttpDelete("{listId:guid}")]
        public async Task<IActionResult> DeleteList(Guid listId, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var deleted = await _shoppingListService.DeleteListAsync(listId, userId.Value, ct);
            if (!deleted) return NotFound("Shopping list not found.");

            return NoContent();
        }

        // ========== Recipe Management ==========

        /// <summary>
        /// Add a recipe's ingredients to the shopping list
        /// </summary>
        [Authorize]
        [HttpPost("{listId:guid}/recipes")]
        public async Task<IActionResult> AddRecipe(Guid listId, [FromBody] AddRecipeToListDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            try
            {
                _logger.LogInformation("Adding recipe {RecipeId} to list {ListId} for user {UserId}", dto.RecipeId, listId, userId);

                var list = await _shoppingListService.AddRecipeToListAsync(listId, userId.Value, dto, ct);
                if (list == null) return NotFound("Shopping list not found.");

                return Ok(list);
            }
            catch (DuplicateRecipeException ex)
            {
                return Conflict(new { error = "duplicate_recipe", message = ex.Message });
            }
            catch (PremiumRequiredException ex)
            {
                return StatusCode(402, new { error = "premium_required", code = ex.LimitCode, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding recipe {RecipeId} to list {ListId}", dto.RecipeId, listId);
                return StatusCode(500, new { error = "server_error", message = "Failed to add recipe to shopping list" });
            }
        }

        /// <summary>
        /// Remove a recipe's ingredients from the shopping list
        /// </summary>
        [Authorize]
        [HttpDelete("{listId:guid}/recipes/{recipeId:guid}")]
        public async Task<IActionResult> RemoveRecipe(Guid listId, Guid recipeId, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var removed = await _shoppingListService.RemoveRecipeFromListAsync(listId, userId.Value, recipeId, ct);
            if (!removed) return NotFound("Shopping list not found.");

            return NoContent();
        }

        /// <summary>
        /// Quick action: Create a new shopping list from a single recipe
        /// </summary>
        [Authorize]
        [HttpPost("from-recipe/{recipeId:guid}")]
        public async Task<IActionResult> CreateFromRecipe(Guid recipeId, [FromQuery] decimal scaleFactor = 1.0m, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            try
            {
                _logger.LogInformation("Creating shopping list from recipe {RecipeId} for user {UserId}", recipeId, userId);

                var list = await _shoppingListService.CreateFromRecipeAsync(userId.Value, recipeId, scaleFactor, ct);
                return CreatedAtAction(nameof(GetList), new { listId = list.Id }, list);
            }
            catch (PremiumRequiredException ex)
            {
                return StatusCode(402, new { error = "premium_required", code = ex.LimitCode, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating shopping list from recipe {RecipeId}", recipeId);
                return StatusCode(500, new { error = "server_error", message = "Failed to create shopping list from recipe" });
            }
        }

        // ========== Item Management ==========

        /// <summary>
        /// Add a manual item to the shopping list
        /// </summary>
        [Authorize]
        [HttpPost("{listId:guid}/items")]
        public async Task<IActionResult> AddItem(Guid listId, [FromBody] AddManualItemDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var item = await _shoppingListService.AddManualItemAsync(listId, userId.Value, dto, ct);
            if (item == null) return NotFound("Shopping list not found.");

            return Ok(item);
        }

        /// <summary>
        /// Update a shopping list item (check/uncheck, quantity, note)
        /// </summary>
        [Authorize]
        [HttpPatch("{listId:guid}/items/{itemId:guid}")]
        public async Task<IActionResult> UpdateItem(Guid listId, Guid itemId, [FromBody] UpdateItemDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var item = await _shoppingListService.UpdateItemAsync(listId, itemId, userId.Value, dto, ct);
            if (item == null) return NotFound("Item not found.");

            return Ok(item);
        }

        /// <summary>
        /// Delete a shopping list item
        /// </summary>
        [Authorize]
        [HttpDelete("{listId:guid}/items/{itemId:guid}")]
        public async Task<IActionResult> DeleteItem(Guid listId, Guid itemId, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var deleted = await _shoppingListService.DeleteItemAsync(listId, itemId, userId.Value, ct);
            if (!deleted) return NotFound("Item not found.");

            return NoContent();
        }

        /// <summary>
        /// Bulk check/uncheck multiple items
        /// </summary>
        [Authorize]
        [HttpPost("{listId:guid}/items/bulk-check")]
        public async Task<IActionResult> BulkCheckItems(Guid listId, [FromBody] BulkCheckItemsDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var count = await _shoppingListService.BulkCheckItemsAsync(listId, userId.Value, dto, ct);
            return Ok(new { updatedCount = count });
        }

        /// <summary>
        /// Clear all checked items from the list
        /// </summary>
        [Authorize]
        [HttpPost("{listId:guid}/items/clear-checked")]
        public async Task<IActionResult> ClearCheckedItems(Guid listId, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var cleared = await _shoppingListService.ClearCheckedItemsAsync(listId, userId.Value, ct);
            if (!cleared) return NotFound("Shopping list not found.");

            return NoContent();
        }

        /// <summary>
        /// Clear all items from the list
        /// </summary>
        [Authorize]
        [HttpPost("{listId:guid}/items/clear-all")]
        public async Task<IActionResult> ClearAllItems(Guid listId, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var cleared = await _shoppingListService.ClearAllItemsAsync(listId, userId.Value, ct);
            if (!cleared) return NotFound("Shopping list not found.");

            return NoContent();
        }

        /// <summary>
        /// Clear all recipes from the list (keeps manually added items)
        /// </summary>
        [Authorize]
        [HttpPost("{listId:guid}/recipes/clear-all")]
        public async Task<IActionResult> ClearAllRecipes(Guid listId, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var cleared = await _shoppingListService.ClearAllRecipesAsync(listId, userId.Value, ct);
            if (!cleared) return NotFound("Shopping list not found.");

            return NoContent();
        }

        // ========== Sharing ==========

        /// <summary>
        /// Enable/disable sharing for a shopping list
        /// </summary>
        [Authorize]
        [HttpPost("{listId:guid}/share")]
        public async Task<IActionResult> SetSharing(Guid listId, [FromBody] ShareListDto dto, CancellationToken ct)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var result = await _shoppingListService.SetSharingAsync(listId, userId.Value, dto, ct);
            if (result.ShareUrl == null && dto.IsShared)
                return NotFound("Shopping list not found.");

            return Ok(result);
        }
    }
}
