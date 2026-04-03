using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>Provides B2B ingredient trending data from the pre-computed <c>IngredientTrendDaily</c> table.</summary>
    public interface IIngredientTrendingService
    {
        Task<IngredientTrendingResponse> GetTrendingAsync(
            IngredientTrendingRequest request,
            CancellationToken ct = default);
    }
}
