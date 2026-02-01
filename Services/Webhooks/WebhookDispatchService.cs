using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Webhooks
{
    /// <summary>
    /// Background service that dispatches pending webhook events.
    /// Polls the WebhookEvents table and sends HTTP POST to subscriber endpoints.
    /// Implements retry with exponential backoff.
    /// </summary>
    public class WebhookDispatchService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<WebhookDispatchService> _logger;
        private const int MaxAttempts = 5;
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

        public WebhookDispatchService(
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpFactory,
            ILogger<WebhookDispatchService> logger)
        {
            _scopeFactory = scopeFactory;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WebhookDispatchService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingEventsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing webhook events");
                }

                await Task.Delay(PollInterval, stoppingToken);
            }
        }

        private async Task ProcessPendingEventsAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlaterlyDbContext>();

            var pendingEvents = await db.WebhookEvents
                .Include(e => e.Subscription)
                .Where(e => e.Status == WebhookEventStatus.Pending &&
                       e.Attempts < MaxAttempts &&
                       (e.NextRetryAt == null || e.NextRetryAt <= DateTime.UtcNow))
                .OrderBy(e => e.CreatedAt)
                .Take(50) // Process up to 50 at a time
                .ToListAsync(ct);

            if (pendingEvents.Count == 0) return;

            _logger.LogInformation("Processing {Count} pending webhook events", pendingEvents.Count);

            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            foreach (var evt in pendingEvents)
            {
                if (evt.Subscription == null || !evt.Subscription.IsActive) continue;

                try
                {
                    var content = new StringContent(evt.Payload, Encoding.UTF8, "application/json");

                    // Add HMAC-SHA256 signature
                    var signature = ComputeHmac(evt.Payload, evt.Subscription.Secret);
                    content.Headers.Add("X-Recipzee-Signature", signature);
                    content.Headers.Add("X-Recipzee-Event", evt.EventType);

                    var response = await client.PostAsync(evt.Subscription.Url, content, ct);

                    if (response.IsSuccessStatusCode)
                    {
                        evt.Status = WebhookEventStatus.Sent;
                        evt.SentAt = DateTime.UtcNow;
                        evt.Subscription.LastTriggeredAt = DateTime.UtcNow;
                        evt.Subscription.ConsecutiveFailures = 0;
                    }
                    else
                    {
                        HandleFailure(evt, $"HTTP {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    HandleFailure(evt, ex.Message);
                }

                evt.Attempts++;
            }

            await db.SaveChangesAsync(ct);
        }

        private static void HandleFailure(WebhookEvent evt, string error)
        {
            evt.LastError = error.Length > 1024 ? error[..1024] : error;

            if (evt.Attempts + 1 >= MaxAttempts)
            {
                evt.Status = WebhookEventStatus.Failed;

                if (evt.Subscription != null)
                {
                    evt.Subscription.ConsecutiveFailures++;

                    // Deactivate after 10 consecutive failures
                    if (evt.Subscription.ConsecutiveFailures >= 10)
                    {
                        evt.Subscription.IsActive = false;
                    }
                }
            }
            else
            {
                // Exponential backoff: 30s, 2m, 8m, 32m
                var delay = TimeSpan.FromSeconds(30 * Math.Pow(4, evt.Attempts));
                evt.NextRetryAt = DateTime.UtcNow.Add(delay);

                if (evt.Subscription != null)
                {
                    evt.Subscription.ConsecutiveFailures++;
                }
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
