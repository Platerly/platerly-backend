using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Safety;
using Platerly.Api.Services.Webhooks;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Webhook Management API — layer: B2B, base route: <c>api/Webhook</c>.
    /// Allows API key holders to register HTTPS endpoints that receive real-time push
    /// notifications when platform trend events occur. Each subscription is scoped to the
    /// API key that created it — callers can only view, delete, and test their own subscriptions.
    /// Supported event types: <c>ingredient.trending</c>, <c>dietary.shift</c>,
    /// <c>region.spike</c>, <c>new.substitution</c>.
    /// Auth: API Key (<see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class WebhookController : B2BControllerBase
    {
        private readonly IWebhookService _webhooks;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(
            IWebhookService webhooks,
            ILogger<WebhookController> logger,
            ISafetyMetaService safetyMeta)
            : base(safetyMeta)
        {
            _webhooks = webhooks;
            _logger = logger;
        }

        /// <summary>
        /// <c>POST api/Webhook/subscribe</c> — Register a webhook URL for one or more event types.
        /// Creates a new subscription scoped to the calling API key. The platform will POST
        /// a JSON payload to the registered URL whenever a matching event is triggered.
        /// </summary>
        /// <param name="request">
        /// Request body with <c>Url</c> (HTTPS endpoint) and <c>EventTypes</c> (non-empty list).
        /// Supported event types: <c>ingredient.trending</c>, <c>dietary.shift</c>,
        /// <c>region.spike</c>, <c>new.substitution</c>.
        /// </param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the created subscription record.<br/>
        /// <c>400 Bad Request</c> — <c>Url</c> is blank, or <c>EventTypes</c> is null/empty.<br/>
        /// <c>401 Unauthorized</c> — missing, invalid, or unresolvable API key.
        /// </returns>
        [HttpPost("subscribe")]
        public async Task<ActionResult> Subscribe(
            [FromBody] WebhookSubscribeRequest request,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.Url))
                return ApiError("Webhook URL is required.");

            if (request.EventTypes == null || request.EventTypes.Count == 0)
                return ApiError("At least one event type is required.");

            var apiKeyId = GetApiKeyId();
            if (apiKeyId == Guid.Empty) return ApiError("API key identity could not be resolved.", 401);

            var result = await _webhooks.SubscribeAsync(apiKeyId, request, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// <c>GET api/Webhook</c> — List all webhook subscriptions for the current API key.
        /// Returns only subscriptions owned by the authenticated API key; other keys' subscriptions
        /// are never visible.
        /// </summary>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the subscription list.<br/>
        /// <c>401 Unauthorized</c> — missing, invalid, or unresolvable API key.
        /// </returns>
        [HttpGet]
        public async Task<ActionResult> List(
            CancellationToken ct = default)
        {
            var apiKeyId = GetApiKeyId();
            if (apiKeyId == Guid.Empty) return ApiError("API key identity could not be resolved.", 401);

            var result = await _webhooks.ListSubscriptionsAsync(apiKeyId, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// <c>DELETE api/Webhook/{id}</c> — Remove a webhook subscription.
        /// The subscription must belong to the calling API key; attempting to delete another
        /// key's subscription returns 404.
        /// </summary>
        /// <param name="id">GUID of the subscription to delete.</param>
        /// <returns>
        /// <c>204 No Content</c> — subscription deleted successfully.<br/>
        /// <c>404 Not Found</c> — subscription does not exist or belongs to a different API key.<br/>
        /// <c>401 Unauthorized</c> — missing, invalid, or unresolvable API key.
        /// </returns>
        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(
            Guid id,
            CancellationToken ct = default)
        {
            var apiKeyId = GetApiKeyId();
            if (apiKeyId == Guid.Empty) return ApiError("API key identity could not be resolved.", 401);

            var deleted = await _webhooks.DeleteSubscriptionAsync(apiKeyId, id, ct);
            if (!deleted) return ApiError("Subscription not found.", 404);
            return NoContent();
        }

        /// <summary>
        /// <c>POST api/Webhook/{id}/test</c> — Send a synthetic test event to a webhook endpoint.
        /// Dispatches a test payload to the registered URL for the specified subscription,
        /// allowing the caller to verify connectivity and payload handling before relying on
        /// real events. The subscription must belong to the calling API key.
        /// </summary>
        /// <param name="id">GUID of the subscription to test.</param>
        /// <returns>
        /// <c>200 OK</c> — <see cref="SafetyApiResponse{T}"/> wrapping the test dispatch result
        /// (includes HTTP status code returned by the target URL).<br/>
        /// <c>401 Unauthorized</c> — missing, invalid, or unresolvable API key.
        /// </returns>
        [HttpPost("{id}/test")]
        public async Task<ActionResult> Test(
            Guid id,
            CancellationToken ct = default)
        {
            var apiKeyId = GetApiKeyId();
            if (apiKeyId == Guid.Empty) return ApiError("API key identity could not be resolved.", 401);

            var result = await _webhooks.TestSubscriptionAsync(apiKeyId, id, ct);
            return SafetyOk(result);
        }
    }
}
