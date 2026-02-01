using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    public interface IRegionalDemandService
    {
        Task<RegionalDemandForecastResponse> GetForecastAsync(
            RegionalDemandForecastRequest request,
            CancellationToken ct = default);

        Task<RegionalComparisonResponse> CompareRegionsAsync(
            RegionalComparisonRequest request,
            CancellationToken ct = default);
    }
}
