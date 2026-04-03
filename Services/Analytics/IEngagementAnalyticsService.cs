using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>Provides B2B engagement analytics: platform-wide overview, by-category breakdown, and by-region breakdown.</summary>
    public interface IEngagementAnalyticsService
    {
        Task<EngagementOverviewResponse> GetOverviewAsync(
            CancellationToken ct = default);

        Task<EngagementByCategoryResponse> GetByCategoryAsync(
            CancellationToken ct = default);

        Task<EngagementByRegionResponse> GetByRegionAsync(
            int take = 50,
            CancellationToken ct = default);
    }
}
