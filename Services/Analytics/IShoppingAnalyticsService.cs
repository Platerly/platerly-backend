using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    public interface IShoppingAnalyticsService
    {
        Task<ShoppingPatternsResponse> GetPatternsAsync(
            int take = 50,
            CancellationToken ct = default);

        Task<MealPlanningPatternsResponse> GetMealPlanningPatternsAsync(
            int take = 20,
            CancellationToken ct = default);
    }
}
