using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Shopping
{
    /// <summary>Provides CRUD for meal plans and entries, with shopping list generation from a plan.</summary>
    public interface IMealPlanService
    {
        // Meal Plan CRUD
        Task<List<MealPlanSummaryDto>> GetUserMealPlansAsync(Guid userId, CancellationToken ct = default);
        Task<MealPlanDto?> GetMealPlanAsync(Guid planId, Guid? userId = null, CancellationToken ct = default);
        Task<MealPlanDto> CreateMealPlanAsync(Guid userId, CreateMealPlanDto dto, CancellationToken ct = default);
        Task<MealPlanDto?> UpdateMealPlanAsync(Guid planId, Guid userId, CreateMealPlanDto dto, CancellationToken ct = default);
        Task<bool> DeleteMealPlanAsync(Guid planId, Guid userId, CancellationToken ct = default);

        // Entry management
        Task<MealPlanEntryDto?> AddEntryAsync(Guid planId, Guid userId, AddMealPlanEntryDto dto, CancellationToken ct = default);
        Task<MealPlanEntryDto?> UpdateEntryAsync(Guid planId, Guid entryId, Guid userId, AddMealPlanEntryDto dto, CancellationToken ct = default);
        Task<bool> DeleteEntryAsync(Guid planId, Guid entryId, Guid userId, CancellationToken ct = default);

        // Generate shopping list
        Task<ShoppingListDto> GenerateShoppingListAsync(Guid planId, Guid userId, GenerateShoppingListFromMealPlanDto dto, CancellationToken ct = default);
    }
}
