using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Analytics
{
    /// <summary>Provides B2B regional demand forecasting and multi-region comparison for ingredient trends.</summary>
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
