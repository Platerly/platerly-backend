using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Webhooks
{
    public class WebhookService : IWebhookService
    {
        private readonly PlaterlyDbContext _db;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<WebhookService> _logger;

        public WebhookService(
            PlaterlyDbContext db,
            IHttpClientFactory httpFactory,
            ILogger<WebhookService> logger)
        {
            _db = db;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        public async Task<WebhookSubscriptionDto> SubscribeAsync(
            Guid apiKeyId, WebhookSubscribeRequest request,
            CancellationToken ct = default)
        {
            // Generate a secret for HMAC signing
            var secretBytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(secretBytes);
            var secret = Convert.ToBase64String(secretBytes);

            var subscription = new WebhookSubscription
            {
                ApiKeyId = apiKeyId,
                Url = request.Url,
                Secret = secret,
                EventTypes = string.Join(",", request.EventTypes),
                IsActive = true
            };

            _db.WebhookSubscriptions.Add(subscription);
            await _db.SaveChangesAsync(ct);

            return new WebhookSubscriptionDto
            {
                Id = subscription.Id,
                Url = subscription.Url,
                EventTypes = request.EventTypes,
                IsActive = subscription.IsActive,
                CreatedAt = subscription.CreatedAt,
                ConsecutiveFailures = 0
            };
        }

        public async Task<WebhookListResponse> ListSubscriptionsAsync(
            Guid apiKeyId,
            CancellationToken ct = default)
        {
            var subs = await _db.WebhookSubscriptions
                .Where(s => s.ApiKeyId == apiKeyId)
                .Select(s => new WebhookSubscriptionDto
                {
                    Id = s.Id,
                    Url = s.Url,
                    EventTypes = s.EventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    IsActive = s.IsActive,
                    CreatedAt = s.CreatedAt,
                    LastTriggeredAt = s.LastTriggeredAt,
                    ConsecutiveFailures = s.ConsecutiveFailures
                })
                .ToListAsync(ct);

            return new WebhookListResponse { Subscriptions = subs };
        }

        public async Task<bool> DeleteSubscriptionAsync(
            Guid apiKeyId, Guid subscriptionId,
            CancellationToken ct = default)
        {
            var sub = await _db.WebhookSubscriptions
                .Where(s => s.Id == subscriptionId && s.ApiKeyId == apiKeyId)
                .FirstOrDefaultAsync(ct);

            if (sub == null) return false;

            _db.WebhookSubscriptions.Remove(sub);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<WebhookTestResponse> TestSubscriptionAsync(
            Guid apiKeyId, Guid subscriptionId,
            CancellationToken ct = default)
        {
            var sub = await _db.WebhookSubscriptions
                .Where(s => s.Id == subscriptionId && s.ApiKeyId == apiKeyId)
                .FirstOrDefaultAsync(ct);

            if (sub == null)
            {
                return new WebhookTestResponse
                {
                    Success = false,
                    Error = "Subscription not found",
                    StatusCode = 404
                };
            }

            var testPayload = JsonSerializer.Serialize(new
            {
                eventType = "test",
                timestamp = DateTime.UtcNow,
                data = new { message = "This is a test webhook from Recipzee." }
            });

            try
            {
                var client = _httpFactory.CreateClient();
                var content = new StringContent(testPayload, Encoding.UTF8, "application/json");

                // Add HMAC signature
                var signature = ComputeHmac(testPayload, sub.Secret);
                content.Headers.Add("X-Recipzee-Signature", signature);

                var response = await client.PostAsync(sub.Url, content, ct);

                return new WebhookTestResponse
                {
                    Success = response.IsSuccessStatusCode,
                    StatusCode = (int)response.StatusCode,
                    Error = response.IsSuccessStatusCode ? null : $"HTTP {response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                return new WebhookTestResponse
                {
                    Success = false,
                    Error = ex.Message,
                    StatusCode = 0
                };
            }
        }

        public async Task EmitEventAsync(
            string eventType, string payloadJson,
            CancellationToken ct = default)
        {
            // Find all active subscriptions that match this event type
            var subs = await _db.WebhookSubscriptions
                .Where(s => s.IsActive && s.EventTypes.Contains(eventType))
                .ToListAsync(ct);

            foreach (var sub in subs)
            {
                _db.WebhookEvents.Add(new WebhookEvent
                {
                    SubscriptionId = sub.Id,
                    EventType = eventType,
                    Payload = payloadJson,
                    Status = WebhookEventStatus.Pending,
                    NextRetryAt = DateTime.UtcNow
                });
            }

            if (subs.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
            }
        }

        private static string ComputeHmac(string payload, string secret)
        {
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(payloadBytes);
            return $"sha256={Convert.ToHexString(hash).ToLower()}";
        }
    }
}
