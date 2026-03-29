using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Services.Safety;
using Platerly.Api.Services.Webhooks;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Webhook Management API.
    /// Subscribe to real-time trend alerts and manage webhook endpoints.
    /// Requires API Key authentication.
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
        /// Register a webhook URL for specific event types.
        /// Event types: ingredient.trending, dietary.shift, region.spike, new.substitution
        /// </summary>
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
        /// List all webhook subscriptions for the current API key.
        /// </summary>
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
        /// Remove a webhook subscription.
        /// </summary>
        /// <param name="id">The subscription ID to delete</param>
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
        /// Send a test event to verify a webhook endpoint is working.
        /// </summary>
        /// <param name="id">The subscription ID to test</param>
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
