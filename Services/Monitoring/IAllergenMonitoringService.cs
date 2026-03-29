using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Monitoring
{
    public interface IAllergenMonitoringService
    {
        Task<MonitoringRegisterResponse> RegisterAsync(
            Guid apiKeyId,
            MonitoringRegisterRequest request,
            CancellationToken ct = default);

        Task<MonitoringListResponse> ListSubscriptionsAsync(
            Guid apiKeyId,
            CancellationToken ct = default);

        Task<bool> DeleteSubscriptionAsync(
            Guid apiKeyId,
            Guid subscriptionId,
            CancellationToken ct = default);
    }
}
