using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Shopping
{
    public interface IShoppingListService
    {
        // Shopping List CRUD
        Task<List<ShoppingListSummaryDto>> GetUserListsAsync(Guid userId, CancellationToken ct = default);
        Task<ShoppingListDto?> GetListAsync(Guid listId, Guid? userId = null, CancellationToken ct = default);
        Task<ShoppingListDto?> GetListByShareTokenAsync(string shareToken, CancellationToken ct = default);
        Task<ShoppingListDto> CreateListAsync(Guid userId, CreateShoppingListDto dto, CancellationToken ct = default);
        Task<ShoppingListDto?> UpdateListAsync(Guid listId, Guid userId, UpdateShoppingListDto dto, CancellationToken ct = default);
        Task<bool> DeleteListAsync(Guid listId, Guid userId, CancellationToken ct = default);

        // Recipe management
        Task<ShoppingListDto?> AddRecipeToListAsync(Guid listId, Guid userId, AddRecipeToListDto dto, CancellationToken ct = default);
        Task<bool> RemoveRecipeFromListAsync(Guid listId, Guid userId, Guid recipeId, CancellationToken ct = default);

        // Item management
        Task<ShoppingListItemDto?> AddManualItemAsync(Guid listId, Guid userId, AddManualItemDto dto, CancellationToken ct = default);
        Task<ShoppingListItemDto?> UpdateItemAsync(Guid listId, Guid itemId, Guid userId, UpdateItemDto dto, CancellationToken ct = default);
        Task<bool> DeleteItemAsync(Guid listId, Guid itemId, Guid userId, CancellationToken ct = default);
        Task<int> BulkCheckItemsAsync(Guid listId, Guid userId, BulkCheckItemsDto dto, CancellationToken ct = default);
        Task<bool> ClearCheckedItemsAsync(Guid listId, Guid userId, CancellationToken ct = default);
        Task<bool> ClearAllItemsAsync(Guid listId, Guid userId, CancellationToken ct = default);
        Task<bool> ClearAllRecipesAsync(Guid listId, Guid userId, CancellationToken ct = default);

        // Sharing
        Task<ShareListResultDto> SetSharingAsync(Guid listId, Guid userId, ShareListDto dto, CancellationToken ct = default);

        // Quick actions
        Task<ShoppingListDto> CreateFromRecipeAsync(Guid userId, Guid recipeId, decimal scaleFactor = 1.0m, CancellationToken ct = default);
    }
}
