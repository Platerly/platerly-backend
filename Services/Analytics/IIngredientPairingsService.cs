using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>Provides B2B ingredient co-occurrence pairing data with affinity scores and nickel classification.</summary>
    public interface IIngredientPairingsService
    {
        Task<IngredientPairingsResponse> GetPairingsAsync(
            IngredientPairingsRequest request,
            CancellationToken ct = default);
    }
}
