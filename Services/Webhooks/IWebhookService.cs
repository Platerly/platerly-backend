using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Webhooks
{
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
