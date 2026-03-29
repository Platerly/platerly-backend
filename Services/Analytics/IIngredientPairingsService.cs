using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    public interface IIngredientPairingsService
    {
        Task<IngredientPairingsResponse> GetPairingsAsync(
            IngredientPairingsRequest request,
            CancellationToken ct = default);
    }
}
