using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>Provides B2B shopping behaviour analytics including list patterns and meal-planning frequency data.</summary>
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
