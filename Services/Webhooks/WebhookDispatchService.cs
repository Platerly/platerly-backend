using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Webhooks
{
    /// <summary>
    /// B2B infrastructure background service — delivers pending webhook events to subscriber endpoints.
    /// Polls <c>WebhookEvents</c> every 30 seconds for rows with <c>Status=Pending</c> and
    /// <c>NextRetryAt &lt;= utcNow</c>.
    ///
    /// Delivery:
    ///   Each event is HTTP-POSTed to the subscriber URL with a 10-second timeout.
    ///   The payload is signed with HMAC-SHA256 (<c>X-Recipzee-Signature: sha256={hex}</c>)
    ///   and the event type is included in <c>X-Recipzee-Event</c>.
    ///
    /// Retry / failure handling (see <see cref="HandleFailure"/>):
    ///   On failure, exponential backoff delays are applied: 30 s, 2 min, 8 min, 32 min
    ///   (formula: 30 × 4^attempt seconds). After <see cref="MaxAttempts"/> (5) failures the
    ///   event is permanently marked <c>Failed</c>. When a subscription accumulates 10 consecutive
    ///   failures across any events, it is deactivated to prevent continued delivery attempts to
    ///   a non-responsive endpoint.
    /// </summary>
    public class WebhookDispatchService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<WebhookDispatchService> _logger;

        /// <summary>Maximum delivery attempts per event before marking it Failed.</summary>
        private const int MaxAttempts = 5;

        /// <summary>How frequently the dispatch loop polls for pending events.</summary>
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

        /// <summary>
        /// Fetches up to 50 pending <c>WebhookEvent</c> rows (ordered oldest-first) that are due for
        /// delivery, attempts delivery to each subscriber endpoint, and persists the updated statuses.
        /// Creates a fresh DI scope per invocation to use a scoped <see cref="PlaterlyDbContext"/>.
        /// </summary>
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

        /// <summary>
        /// Applies failure handling to a webhook event after a failed delivery attempt.
        /// If this is the final attempt (<see cref="MaxAttempts"/> reached), marks the event
        /// as <c>Failed</c> and increments the subscription's consecutive-failure counter,
        /// deactivating the subscription if the counter reaches 10.
        /// Otherwise, schedules the next retry using exponential backoff:
        /// delay = 30 × 4^attempts seconds (30 s → 2 min → 8 min → 32 min).
        /// </summary>
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

        /// <summary>
        /// Computes an HMAC-SHA256 signature over the payload string using the subscription secret.
        /// Returns <c>sha256={lowercasehex}</c> for use in the <c>X-Recipzee-Signature</c> header.
        /// </summary>
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
