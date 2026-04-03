using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>Provides B2B deep-dive analytics for individual dietary tags and cross-tag overlap data.</summary>
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
