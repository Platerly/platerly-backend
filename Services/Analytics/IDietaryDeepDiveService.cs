using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    public interface IDietaryDeepDiveService
    {
        Task<DietaryTagDetailResponse> GetTagDetailAsync(
            string tag,
            CancellationToken ct = default);

        Task<DietaryCrossoverResponse> GetCrossoverAsync(
            int take = 50,
            CancellationToken ct = default);
    }
}
