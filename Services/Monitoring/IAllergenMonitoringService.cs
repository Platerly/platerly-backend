using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Monitoring
{
    /// <summary>Manages B2B allergen monitoring subscriptions; change detection and webhook dispatch are handled by <c>AllergenMonitoringBackgroundService</c>.</summary>
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
