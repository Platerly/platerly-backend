using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Exceptions;
using Platerly.Api.Models;
using System.Security.Cryptography;

namespace Platerly.Api.Services.Shopping
{
    /// <summary>
    /// B2C user logic layer — manages shopping lists including creation, recipe-ingredient aggregation,
    /// item-level CRUD, sharing, and quantity consolidation.
    /// Depends on <see cref="PlaterlyDbContext"/> and <see cref="IQuantityParserService"/>.
    ///
    /// Tier limits: free users are limited to 1 active shopping list.
    /// Premium users have unlimited lists.
    ///
    /// When a recipe is added, its ingredients are extracted, quantities parsed and scaled,
    /// and consolidated (summed) with any existing items of the same name already in the list.
    /// Share tokens are URL-safe base64 strings generated from 24 cryptographic random bytes.
    /// </summary>
    public class ShoppingListService : IShoppingListService
    {
        private readonly PlaterlyDbContext _db;
        private readonly IQuantityParserService _quantityParser;
        private readonly ILogger<ShoppingListService> _logger;

        /// <summary>Maximum number of shopping lists a free-tier user may have active simultaneously.</summary>
        private const int FREE_MAX_LISTS = 1;

        public ShoppingListService(
            PlaterlyDbContext db,
            IQuantityParserService quantityParser,
            ILogger<ShoppingListService> logger)
        {
            _db = db;
            _quantityParser = quantityParser;
            _logger = logger;
        }

        private async Task<bool> IsUserPremiumAsync(Guid userId, CancellationToken ct = default)
        {
            return await _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.IsPremium)
                .FirstOrDefaultAsync(ct);
        }

        private async Task<int> GetUserListCountAsync(Guid userId, CancellationToken ct = default)
        {
            return await _db.ShoppingLists
                .CountAsync(l => l.UserId == userId && !l.IsDeleted, ct);
        }

        private async Task<int> GetListRecipeCountAsync(Guid listId, CancellationToken ct = default)
        {
            return await _db.ShoppingListRecipes
                .CountAsync(sr => sr.ShoppingListId == listId, ct);
        }

        /// <summary>
        /// Generates a unique shopping list name for a user
        /// </summary>
        private async Task<string> GenerateUniqueNameAsync(Guid userId, string baseName, CancellationToken ct = default)
        {
            var existingNames = await _db.ShoppingLists
                .Where(l => l.UserId == userId && !l.IsDeleted)
                .Select(l => l.Name.ToLower())
                .ToListAsync(ct);

            var name = baseName;
            var counter = 1;

            while (existingNames.Contains(name.ToLower()))
            {
                counter++;
                name = $"{baseName} ({counter})";
            }

            return name;
        }

        public async Task<List<ShoppingListSummaryDto>> GetUserListsAsync(Guid userId, CancellationToken ct = default)
        {
            return await _db.ShoppingLists
                .Where(l => l.UserId == userId && !l.IsDeleted)
                .OrderByDescending(l => l.UpdatedAt ?? l.CreatedAt)
                .Select(l => new ShoppingListSummaryDto
                {
                    Id = l.Id,
                    Name = l.Name,
                    TotalItems = l.Items.Count,
                    CheckedItems = l.Items.Count(i => i.IsChecked),
                    RecipeCount = l.SourceRecipes.Count,
                    RecipeIds = l.SourceRecipes.Select(sr => sr.RecipeId).ToList(),
                    CreatedAt = l.CreatedAt,
                    UpdatedAt = l.UpdatedAt
                })
                .AsNoTracking()
                .ToListAsync(ct);
        }

        public async Task<ShoppingListDto?> GetListAsync(Guid listId, Guid? userId = null, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .Include(l => l.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Category).ThenBy(i => i.Name))
                    .ThenInclude(i => i.Sources)
                        .ThenInclude(s => s.Recipe)
                .Include(l => l.SourceRecipes)
                    .ThenInclude(sr => sr.Recipe)
                .Include(l => l.MealPlan)
                .Where(l => l.Id == listId && !l.IsDeleted)
                .FirstOrDefaultAsync(ct);

            if (list == null) return null;

            // Check access
            if (!list.IsShared && userId.HasValue && list.UserId != userId.Value)
                return null;

            return MapToDto(list);
        }

        public async Task<ShoppingListDto?> GetListByShareTokenAsync(string shareToken, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .Include(l => l.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Category).ThenBy(i => i.Name))
                    .ThenInclude(i => i.Sources)
                        .ThenInclude(s => s.Recipe)
                .Include(l => l.SourceRecipes)
                    .ThenInclude(sr => sr.Recipe)
                .Where(l => l.ShareToken == shareToken && l.IsShared && !l.IsDeleted)
                .FirstOrDefaultAsync(ct);

            return list == null ? null : MapToDto(list);
        }

        public async Task<ShoppingListDto> CreateListAsync(Guid userId, CreateShoppingListDto dto, CancellationToken ct = default)
        {
            // Check premium limits for creating multiple lists
            var isPremium = await IsUserPremiumAsync(userId, ct);
            if (!isPremium)
            {
                var existingCount = await GetUserListCountAsync(userId, ct);
                if (existingCount >= FREE_MAX_LISTS)
                {
                    throw new PremiumRequiredException(
                        "Free users can only create 1 shopping list. Upgrade to Premium for unlimited lists.",
                        "shopping_list_limit");
                }
            }

            // Ensure unique name
            var uniqueName = await GenerateUniqueNameAsync(userId, dto.Name ?? "My Shopping List", ct);

            var list = new ShoppingList
            {
                UserId = userId,
                Name = uniqueName,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _db.ShoppingLists.Add(list);
            await _db.SaveChangesAsync(ct);

            // Add initial recipe if specified
            if (dto.RecipeId.HasValue)
            {
                await AddRecipeIngredientsToListAsync(list, dto.RecipeId.Value, dto.ScaleFactor, ct);
                await _db.SaveChangesAsync(ct);
            }

            return (await GetListAsync(list.Id, userId, ct))!;
        }

        public async Task<ShoppingListDto?> UpdateListAsync(Guid listId, Guid userId, UpdateShoppingListDto dto, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId && !l.IsDeleted, ct);

            if (list == null) return null;

            if (dto.Name != null) list.Name = dto.Name;
            if (dto.Notes != null) list.Notes = dto.Notes;
            list.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return await GetListAsync(listId, userId, ct);
        }

        public async Task<bool> DeleteListAsync(Guid listId, Guid userId, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId && !l.IsDeleted, ct);

            if (list == null) return false;

            list.IsDeleted = true;
            list.DeletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<ShoppingListDto?> AddRecipeToListAsync(Guid listId, Guid userId, AddRecipeToListDto dto, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .Include(l => l.Items)
                    .ThenInclude(i => i.Sources)
                .Include(l => l.SourceRecipes)
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId && !l.IsDeleted, ct);

            if (list == null) return null;

            // Check if recipe already added
            if (list.SourceRecipes.Any(sr => sr.RecipeId == dto.RecipeId))
            {
                throw new DuplicateRecipeException(
                    "This recipe is already in the shopping list.",
                    dto.RecipeId);
            }

            await AddRecipeIngredientsToListAsync(list, dto.RecipeId, dto.ScaleFactor, ct);

            list.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return await GetListAsync(listId, userId, ct);
        }

        public async Task<bool> RemoveRecipeFromListAsync(Guid listId, Guid userId, Guid recipeId, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .Include(l => l.Items)
                    .ThenInclude(i => i.Sources)
                .Include(l => l.SourceRecipes)
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId && !l.IsDeleted, ct);

            if (list == null) return false;

            // Remove the recipe link
            var recipeLink = list.SourceRecipes.FirstOrDefault(sr => sr.RecipeId == recipeId);
            if (recipeLink != null)
            {
                _db.ShoppingListRecipes.Remove(recipeLink);
            }

            // Remove sources from items, and items that become empty
            foreach (var item in list.Items.ToList())
            {
                var sources = item.Sources.Where(s => s.RecipeId == recipeId).ToList();
                foreach (var source in sources)
                {
                    _db.ShoppingListItemSources.Remove(source);
                }

                // Recalculate item quantity
                var remainingSources = item.Sources.Where(s => s.RecipeId != recipeId).ToList();
                if (!remainingSources.Any() && !item.IsManuallyAdded)
                {
                    _db.ShoppingListItems.Remove(item);
                }
                else if (remainingSources.Any())
                {
                    // Reconsolidate
                    var quantities = remainingSources
                        .Select(s => _quantityParser.Parse(s.OriginalQuantity))
                        .ToList();
                    var consolidated = _quantityParser.Consolidate(quantities);
                    item.Quantity = consolidated.DisplayText;
                    item.NumericAmount = consolidated.TotalAmount;
                    item.Unit = consolidated.Unit;
                }
            }

            list.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<ShoppingListItemDto?> AddManualItemAsync(Guid listId, Guid userId, AddManualItemDto dto, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId && !l.IsDeleted, ct);

            if (list == null) return null;

            var parsed = _quantityParser.Parse(dto.Quantity);
            var maxSort = await _db.ShoppingListItems
                .Where(i => i.ShoppingListId == listId)
                .MaxAsync(i => (int?)i.SortOrder, ct) ?? 0;

            var item = new ShoppingListItem
            {
                ShoppingListId = listId,
                Name = dto.Name,
                Quantity = dto.Quantity,
                NumericAmount = parsed.Amount,
                Unit = parsed.Unit,
                Category = dto.Category,
                Note = dto.Note,
                IsManuallyAdded = true,
                SortOrder = maxSort + 1,
                CreatedAt = DateTime.UtcNow
            };

            _db.ShoppingListItems.Add(item);
            list.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return MapItemToDto(item);
        }

        public async Task<ShoppingListItemDto?> UpdateItemAsync(Guid listId, Guid itemId, Guid userId, UpdateItemDto dto, CancellationToken ct = default)
        {
            var item = await _db.ShoppingListItems
                .Include(i => i.ShoppingList)
                .FirstOrDefaultAsync(i => i.Id == itemId && i.ShoppingListId == listId, ct);

            if (item?.ShoppingList == null || item.ShoppingList.UserId != userId) return null;

            if (dto.IsChecked.HasValue)
            {
                item.IsChecked = dto.IsChecked.Value;
                item.CheckedAt = dto.IsChecked.Value ? DateTime.UtcNow : null;
            }

            if (dto.Quantity != null)
            {
                var parsed = _quantityParser.Parse(dto.Quantity);
                item.Quantity = dto.Quantity;
                item.NumericAmount = parsed.Amount;
                item.Unit = parsed.Unit;
            }

            if (dto.Note != null) item.Note = dto.Note;
            if (dto.SortOrder.HasValue) item.SortOrder = dto.SortOrder.Value;

            item.UpdatedAt = DateTime.UtcNow;
            item.ShoppingList.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return MapItemToDto(item);
        }

        public async Task<bool> DeleteItemAsync(Guid listId, Guid itemId, Guid userId, CancellationToken ct = default)
        {
            var item = await _db.ShoppingListItems
                .Include(i => i.ShoppingList)
                .FirstOrDefaultAsync(i => i.Id == itemId && i.ShoppingListId == listId, ct);

            if (item?.ShoppingList == null || item.ShoppingList.UserId != userId) return false;

            _db.ShoppingListItems.Remove(item);
            item.ShoppingList.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<int> BulkCheckItemsAsync(Guid listId, Guid userId, BulkCheckItemsDto dto, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId && !l.IsDeleted, ct);

            if (list == null) return 0;

            var now = DateTime.UtcNow;
            var count = await _db.ShoppingListItems
                .Where(i => i.ShoppingListId == listId && dto.ItemIds.Contains(i.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.IsChecked, dto.IsChecked)
                    .SetProperty(i => i.CheckedAt, dto.IsChecked ? now : (DateTime?)null)
                    .SetProperty(i => i.UpdatedAt, now), ct);

            list.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return count;
        }

        public async Task<bool> ClearCheckedItemsAsync(Guid listId, Guid userId, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId && !l.IsDeleted, ct);

            if (list == null) return false;

            await _db.ShoppingListItems
                .Where(i => i.ShoppingListId == listId && i.IsChecked)
                .ExecuteDeleteAsync(ct);

            list.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> ClearAllItemsAsync(Guid listId, Guid userId, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId && !l.IsDeleted, ct);

            if (list == null) return false;

            // Delete all item sources first
            await _db.ShoppingListItemSources
                .Where(s => s.ShoppingListItem.ShoppingListId == listId)
                .ExecuteDeleteAsync(ct);

            // Delete all items
            await _db.ShoppingListItems
                .Where(i => i.ShoppingListId == listId)
                .ExecuteDeleteAsync(ct);

            list.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> ClearAllRecipesAsync(Guid listId, Guid userId, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .Include(l => l.Items)
                    .ThenInclude(i => i.Sources)
                .Include(l => l.SourceRecipes)
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId && !l.IsDeleted, ct);

            if (list == null) return false;

            // Remove all recipe links
            foreach (var recipeLink in list.SourceRecipes.ToList())
            {
                _db.ShoppingListRecipes.Remove(recipeLink);
            }

            // Remove all recipe-sourced items (keep manually added items)
            foreach (var item in list.Items.ToList())
            {
                // Remove all sources
                foreach (var source in item.Sources.ToList())
                {
                    _db.ShoppingListItemSources.Remove(source);
                }

                // Remove the item if it wasn't manually added
                if (!item.IsManuallyAdded)
                {
                    _db.ShoppingListItems.Remove(item);
                }
            }

            list.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<ShareListResultDto> SetSharingAsync(Guid listId, Guid userId, ShareListDto dto, CancellationToken ct = default)
        {
            var list = await _db.ShoppingLists
                .FirstOrDefaultAsync(l => l.Id == listId && l.UserId == userId && !l.IsDeleted, ct);

            if (list == null)
                return new ShareListResultDto { IsShared = false };

            list.IsShared = dto.IsShared;
            list.AllowCollaborativeEdit = dto.AllowCollaborativeEdit;

            if (dto.IsShared && string.IsNullOrEmpty(list.ShareToken))
            {
                // Generate new share token
                list.ShareToken = GenerateShareToken();
            }
            else if (!dto.IsShared)
            {
                // Optionally clear token on unshare (or keep for later re-sharing)
                // list.ShareToken = null;
            }

            list.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new ShareListResultDto
            {
                IsShared = list.IsShared,
                ShareToken = list.ShareToken,
                ShareUrl = list.IsShared && list.ShareToken != null
                    ? $"/shopping-list/shared/{list.ShareToken}"
                    : null
            };
        }

        public async Task<ShoppingListDto> CreateFromRecipeAsync(Guid userId, Guid recipeId, decimal scaleFactor = 1.0m, CancellationToken ct = default)
        {
            // Check premium limits for creating multiple lists
            var isPremium = await IsUserPremiumAsync(userId, ct);
            if (!isPremium)
            {
                var existingCount = await GetUserListCountAsync(userId, ct);
                if (existingCount >= FREE_MAX_LISTS)
                {
                    throw new PremiumRequiredException(
                        "Free users can only create 1 shopping list. Upgrade to Premium for unlimited lists.",
                        "shopping_list_limit");
                }
            }

            var recipeTitle = await _db.Recipes
                .Where(r => r.Id == recipeId)
                .Select(r => r.Title)
                .FirstOrDefaultAsync(ct);

            // Ensure unique name
            var baseName = $"Shopping for {recipeTitle ?? "Recipe"}";
            var uniqueName = await GenerateUniqueNameAsync(userId, baseName, ct);

            var list = new ShoppingList
            {
                UserId = userId,
                Name = uniqueName,
                CreatedAt = DateTime.UtcNow
            };

            _db.ShoppingLists.Add(list);
            await _db.SaveChangesAsync(ct);

            await AddRecipeIngredientsToListAsync(list, recipeId, scaleFactor, ct);
            await _db.SaveChangesAsync(ct);

            return (await GetListAsync(list.Id, userId, ct))!;
        }

        // ========== Private Helpers ==========

        private async Task AddRecipeIngredientsToListAsync(ShoppingList list, Guid recipeId, decimal scaleFactor, CancellationToken ct)
        {
            var recipe = await _db.Recipes
                .Include(r => r.Ingredients)
                    .ThenInclude(i => i.GlobalIngredient)
                        .ThenInclude(g => g!.Category)
                .FirstOrDefaultAsync(r => r.Id == recipeId, ct);

            if (recipe?.Ingredients == null || !recipe.Ingredients.Any()) return;

            // Ensure collections are initialized
            list.Items ??= new List<ShoppingListItem>();
            list.SourceRecipes ??= new List<ShoppingListRecipe>();

            // Add recipe to source list
            var recipeLink = new ShoppingListRecipe
            {
                ShoppingListId = list.Id,
                RecipeId = recipeId,
                ScaleFactor = scaleFactor,
                AddedAt = DateTime.UtcNow
            };
            _db.ShoppingListRecipes.Add(recipeLink);

            foreach (var ingredient in recipe.Ingredients)
            {
                var name = ingredient.GlobalIngredient?.Name ?? ingredient.Name;
                if (string.IsNullOrWhiteSpace(name)) continue; // Skip empty ingredient names

                var normalizedName = name.ToLowerInvariant().Trim();

                // Check if this ingredient already exists in the list
                var existingItem = list.Items?
                    .FirstOrDefault(i => !string.IsNullOrEmpty(i.Name) && i.Name.ToLowerInvariant().Trim() == normalizedName);

                var parsed = _quantityParser.Parse(ingredient.Quantity);
                var scaledAmount = parsed.Amount.HasValue ? parsed.Amount.Value * scaleFactor : (decimal?)null;

                if (existingItem != null)
                {
                    // Ensure Sources collection is initialized
                    existingItem.Sources ??= new List<ShoppingListItemSource>();

                    // Consolidate quantities
                    var source = new ShoppingListItemSource
                    {
                        ShoppingListItemId = existingItem.Id,
                        RecipeId = recipeId,
                        OriginalQuantity = ingredient.Quantity,
                        ScaleFactor = scaleFactor,
                        NumericAmount = scaledAmount,
                        Unit = parsed.Unit
                    };
                    existingItem.Sources.Add(source);
                    _db.ShoppingListItemSources.Add(source); // Explicitly track new source

                    // Recalculate consolidated quantity
                    var allQuantities = existingItem.Sources
                        .Select(s => new ParsedQuantity
                        {
                            Amount = s.NumericAmount,
                            Unit = s.Unit,
                            OriginalText = s.OriginalQuantity,
                            WasParsed = s.NumericAmount.HasValue
                        })
                        .ToList();

                    var consolidated = _quantityParser.Consolidate(allQuantities);
                    existingItem.Quantity = consolidated.DisplayText;
                    existingItem.NumericAmount = consolidated.TotalAmount;
                    existingItem.Unit = consolidated.Unit;
                    existingItem.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    // Create new item
                    var scaledQuantity = scaledAmount.HasValue
                        ? _quantityParser.Format(scaledAmount.Value, parsed.Unit)
                        : ingredient.Quantity;

                    var item = new ShoppingListItem
                    {
                        ShoppingListId = list.Id,
                        GlobalIngredientId = ingredient.GlobalIngredientId,
                        Name = name,
                        Quantity = scaledQuantity,
                        NumericAmount = scaledAmount,
                        Unit = parsed.Unit,
                        Category = ingredient.GlobalIngredient?.Category?.Name,
                        SortOrder = list.Items.Count,
                        CreatedAt = DateTime.UtcNow
                    };

                    item.Sources.Add(new ShoppingListItemSource
                    {
                        RecipeId = recipeId,
                        OriginalQuantity = ingredient.Quantity,
                        ScaleFactor = scaleFactor,
                        NumericAmount = scaledAmount,
                        Unit = parsed.Unit
                    });

                    list.Items.Add(item);
                    _db.Set<ShoppingListItem>().Add(item); // Explicitly track the new item
                }
            }
        }

        private static string GenerateShareToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(24);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

        private ShoppingListDto MapToDto(ShoppingList list)
        {
            return new ShoppingListDto
            {
                Id = list.Id,
                Name = list.Name,
                Notes = list.Notes,
                IsShared = list.IsShared,
                ShareUrl = list.IsShared && list.ShareToken != null
                    ? $"/shopping-list/shared/{list.ShareToken}"
                    : null,
                AllowCollaborativeEdit = list.AllowCollaborativeEdit,
                MealPlanId = list.MealPlanId,
                MealPlanName = list.MealPlan?.Name,
                Items = list.Items.Select(MapItemToDto).ToList(),
                SourceRecipes = list.SourceRecipes.Select(sr => new ShoppingListRecipeDto
                {
                    RecipeId = sr.RecipeId,
                    Title = sr.Recipe?.Title ?? "",
                    ThumbnailUrl = sr.Recipe?.ImageUrl,
                    ScaleFactor = sr.ScaleFactor,
                    Servings = sr.Servings,
                    AddedAt = sr.AddedAt
                }).ToList(),
                TotalItems = list.Items.Count,
                CheckedItems = list.Items.Count(i => i.IsChecked),
                CreatedAt = list.CreatedAt,
                UpdatedAt = list.UpdatedAt
            };
        }

        private static ShoppingListItemDto MapItemToDto(ShoppingListItem item)
        {
            return new ShoppingListItemDto
            {
                Id = item.Id,
                GlobalIngredientId = item.GlobalIngredientId,
                Name = item.Name,
                Quantity = item.Quantity,
                NumericAmount = item.NumericAmount,
                Unit = item.Unit,
                Category = item.Category,
                AisleHint = item.AisleHint,
                IsChecked = item.IsChecked,
                CheckedAt = item.CheckedAt,
                Note = item.Note,
                SortOrder = item.SortOrder,
                IsManuallyAdded = item.IsManuallyAdded,
                Sources = item.Sources.Select(s => new ItemSourceDto
                {
                    RecipeId = s.RecipeId,
                    RecipeTitle = s.Recipe?.Title ?? "",
                    OriginalQuantity = s.OriginalQuantity,
                    ScaleFactor = s.ScaleFactor
                }).ToList()
            };
        }
    }
}
