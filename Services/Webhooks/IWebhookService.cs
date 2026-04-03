using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Webhooks
{
    /// <summary>Manages B2B webhook subscriptions and emits events to matching subscribers; delivery is handled by <c>WebhookDispatchService</c>.</summary>
    public interface IWebhookService
    {
        Task<WebhookSubscriptionDto> SubscribeAsync(
            Guid apiKeyId, WebhookSubscribeRequest request,
            CancellationToken ct = default);

        Task<WebhookListResponse> ListSubscriptionsAsync(
            Guid apiKeyId,
            CancellationToken ct = default);

        Task<bool> DeleteSubscriptionAsync(
            Guid apiKeyId, Guid subscriptionId,
            CancellationToken ct = default);

        Task<WebhookTestResponse> TestSubscriptionAsync(
            Guid apiKeyId, Guid subscriptionId,
            CancellationToken ct = default);

        /// <summary>
        /// Emit a webhook event to all subscriptions that match the event type
        /// </summary>
        Task EmitEventAsync(
            string eventType, string payloadJson,
            CancellationToken ct = default);
    }
}
