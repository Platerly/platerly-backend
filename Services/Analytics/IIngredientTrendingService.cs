using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    public interface IIngredientTrendingService
    {
        Task<IngredientTrendingResponse> GetTrendingAsync(
            IngredientTrendingRequest request,
            CancellationToken ct = default);
    }
}
