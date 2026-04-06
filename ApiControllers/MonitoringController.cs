using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Models;
using Platerly.Api.Services.Monitoring;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Allergen Change Monitoring API. Lets customers subscribe to database-level
    /// allergen change notifications for specific ingredients via webhooks.
    ///
    /// Base route: /api/b2b/monitoring
    /// Auth requirement: API Key (<c>ApiKeyAuthenticationOptions.DefaultScheme</c>).
    ///
    /// How it works:
    ///   1. Customer registers a subscription with an ingredient list and a webhook URL.
    ///   2. <c>AllergenMonitoringBackgroundService</c> checks for allergen profile changes
    ///      approximately every 60 minutes.
    ///   3. When a change is detected, a signed POST is dispatched to the customer's URL
    ///      with a full diff (added / removed / modified allergens per ingredient).
    ///   4. Webhooks are signed with HMAC-SHA256; the secret is in the X-Recipzee-Signature
    ///      header and matches <c>HMAC-SHA256(webhookSecret, rawRequestBody)</c>.
    ///   5. The <c>webhookSecret</c> is returned ONLY in the registration response —
    ///      it cannot be retrieved again. If lost, delete and re-register.
    ///   6. Delivery is retried up to 5 times with exponential backoff on failure.
    ///
    /// Limits: max 50 active subscriptions per API key; max 100 ingredients per subscription.
    ///
    /// IMPORTANT: Monitoring reflects DB-level allergen record changes, not physical product
    /// reformulations. DB updates may lag behind real-world supplier changes.
    ///
    /// POST   /register           — register new subscription
    /// GET    /subscriptions      — list active subscriptions (secrets not included)
    /// DELETE /subscriptions/{id} — remove subscription
    /// </summary>
    [ApiController]
    [Route("api/b2b/monitoring")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class MonitoringController : B2BControllerBase
    {
        private readonly IAllergenMonitoringService _monitoring;
        private readonly ISafetyAuditService _audit;
        private readonly ILogger<MonitoringController> _logger;
        private readonly IConfiguration _config;

        public MonitoringController(
            IAllergenMonitoringService monitoring,
            ISafetyAuditService audit,
            ISafetyMetaService safetyMeta,
            IConfiguration config,
            ILogger<MonitoringController> logger)
            : base(safetyMeta)
        {
            _monitoring = monitoring;
            _audit = audit;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Register a new allergen change monitoring subscription.
        ///
        /// Watches the specified ingredients and fires a webhook to webhookUrl whenever
        /// any ingredient's allergen profile changes in the Platerly database.
        ///
        /// The webhook payload (POST to your URL) contains:
        ///   • eventType: "allergen.change"
        ///   • subscriptionId
        ///   • detectedAt (UTC ISO-8601)
        ///   • summary (human-readable)
        ///   • changes[] — per-ingredient diffs with addedAllergens, removedAllergens, modifiedAllergens
        ///
        /// Verify delivery by computing:
        ///   HMAC-SHA256(webhookSecret, rawRequestBody) → compare to X-Recipzee-Signature header.
        ///
        /// IMPORTANT: The webhookSecret is returned ONLY in this response.
        /// Store it securely — it cannot be retrieved again.
        /// Changes are checked approximately every 60 minutes.
        /// </summary>
        [HttpPost("register")]
        public async Task<ActionResult> Register(
            [FromBody] MonitoringRegisterRequest request,
            CancellationToken ct = default)
        {
            if (!HasScope("clinical"))
                return ApiError(
                    "The 'clinical' scope is required for this endpoint. " +
                    "Upgrade to the Clinical Research tier at recipzee.com/api",
                    403);

            var sw = Stopwatch.StartNew();

            // ── Validate ────────────────────────────────────────────────────────

            if (request.Ingredients == null || request.Ingredients.Count == 0)
                return ApiError("At least one ingredient is required.");

            if (request.Ingredients.Count > 100)
                return ApiError("Maximum 100 ingredients per monitoring subscription.");

            if (string.IsNullOrWhiteSpace(request.WebhookUrl))
                return ApiError("webhookUrl is required.");

            if (!Uri.TryCreate(request.WebhookUrl, UriKind.Absolute, out var webhookUri) ||
                (webhookUri.Scheme != Uri.UriSchemeHttps && webhookUri.Scheme != Uri.UriSchemeHttp))
            {
                return ApiError("webhookUrl must be a valid HTTP or HTTPS URL.");
            }

            if (request.WebhookUrl.Length > 1024)
                return ApiError("webhookUrl must not exceed 1024 characters.");

            if (request.AllergenTypesFilter?.Count > 20)
                return ApiError("allergenTypesFilter must not exceed 20 entries.");

            // Enforce max subscriptions per API key (prevent abuse)
            var apiKeyId = GetApiKeyId();
            var existingCount = await CountActiveSubscriptionsAsync(apiKeyId, ct);
            if (existingCount >= 50)
                return ApiError("Maximum 50 active monitoring subscriptions per API key.", 429);

            // ── Register ─────────────────────────────────────────────────────────

            var result = await _monitoring.RegisterAsync(apiKeyId, request, ct);
            sw.Stop();

            // ── Phase 2 safety metadata ──────────────────────────────────────────

            var alerts = new List<SafetyAlertDto>();

            if (result.UnmatchedIngredients.Any())
            {
                decimal unmatchedPct = (decimal)result.UnmatchedIngredients.Count /
                                       request.Ingredients.Count * 100;

                alerts.Add(new SafetyAlertDto
                {
                    Severity = unmatchedPct >= 50 ? "WARNING" : "INFO",
                    Code = "UNMATCHED_INGREDIENTS",
                    Message = $"{result.UnmatchedIngredients.Count} ingredient(s) were not found in the database " +
                              $"and will not be monitored: {string.Join(", ", result.UnmatchedIngredients.Take(10))}. " +
                              "They will still be stored in the subscription — if they are added to the database " +
                              "in the future, they will begin generating change events automatically."
                });
            }

            if (result.InitialAllergenCount == 0 && !result.UnmatchedIngredients.Any())
            {
                alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "NO_INITIAL_ALLERGENS",
                    Message = "No allergen records were found for the monitored ingredients at registration time. " +
                              "This is a valid baseline — any allergens added to these ingredients in the future " +
                              "will trigger notifications."
                });
            }

            alerts.Add(new SafetyAlertDto
            {
                Severity = "INFO",
                Code = "CHECK_FREQUENCY",
                Message = "Allergen changes are checked approximately every 60 minutes. " +
                          "There may be up to a 1-hour delay between a database update and webhook delivery. " +
                          "Webhooks are retried up to 5 times with exponential backoff on delivery failure."
            });

            alerts.Add(new SafetyAlertDto
            {
                Severity = "INFO",
                Code = "SECRET_NOT_RECOVERABLE",
                Message = "The webhookSecret in this response is shown only once. " +
                          "Store it securely to verify incoming webhook signatures. " +
                          "If lost, delete this subscription and register a new one."
            });

            bool hasWarnings = alerts.Any(a => a.Severity == "WARNING");

            var safetyMeta = new SafetyMeta
            {
                Disclaimer =
                    "Allergen change monitoring reflects changes to the Platerly/Recipzee ingredient database, " +
                    "not changes to physical product formulations. Database updates are made on a best-efforts basis " +
                    "and may lag behind real-world supplier reformulations. This service is not a substitute for " +
                    "direct supplier allergen declarations or regulatory compliance processes. " +
                    "Recipzee / Platerly Inc. accepts no liability for allergen incidents arising from delayed " +
                    "or missed change notifications.",
                Confidence = new ConfidenceInfo
                {
                    Level = hasWarnings ? "MEDIUM" : "HIGH",
                    Score = hasWarnings ? 70 : 95,
                    Explanation = $"{result.MonitoredIngredients.Count - result.UnmatchedIngredients.Count} of " +
                                  $"{result.MonitoredIngredients.Count} ingredients resolved in the database. " +
                                  $"Initial allergen snapshot contains {result.InitialAllergenCount} allergen record(s)."
                },
                DataFreshness = new DataFreshnessInfo
                {
                    SourceVersion = "Platerly Allergen Database v1.0",
                    SourceYear = 2025,
                    SourceCitation = "Platerly/Recipzee GlobalIngredientAllergen table, " +
                                     "populated from FDA Big 9, EU Big 14, and CFIA priority allergen lists.",
                    IsStale = false,
                    LastImportedAt = _config["B2BApi:AllergenDataLastImportedAt"] ?? "2026-01-15"
                },
                Alerts = alerts,
                GeneratedAt = DateTime.UtcNow
            };

            var response = new SafetyApiResponse<MonitoringRegisterResponse>
            {
                Data   = result,
                Safety = safetyMeta
            };

            _ = _audit.LogAsync(new SafetyAuditLog
            {
                HttpMethod = "POST",
                Endpoint = "/api/b2b/monitoring/register",
                ApiKeyId = apiKeyId == Guid.Empty ? null : apiKeyId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString() is { Length: > 512 } ua
                    ? ua[..512] : HttpContext.Request.Headers.UserAgent.ToString(),
                ResponseStatusCode = 200,
                ResponseTimeMs = sw.ElapsedMilliseconds,
                AlertCount = alerts.Count,
                HighestAlertSeverity = hasWarnings ? "WARNING" : "INFO",
                IngredientContext = string.Join(", ", request.Ingredients.Take(20)) is { Length: > 1024 } ic
                    ? ic[..1024] : string.Join(", ", request.Ingredients.Take(20))
            }, CancellationToken.None);

            return Ok(response);
        }

        /// <summary>
        /// List all active allergen monitoring subscriptions for this API key.
        /// Webhook secrets are not included in list responses.
        /// </summary>
        [HttpGet("subscriptions")]
        public async Task<ActionResult> ListSubscriptions(CancellationToken ct = default)
        {
            if (!HasScope("clinical"))
                return ApiError(
                    "The 'clinical' scope is required for this endpoint. " +
                    "Upgrade to the Clinical Research tier at recipzee.com/api",
                    403);

            var apiKeyId = GetApiKeyId();
            var result = await _monitoring.ListSubscriptionsAsync(apiKeyId, ct);
            return SafetyOk(result);
        }

        /// <summary>
        /// Delete an allergen monitoring subscription.
        /// The linked webhook subscription is also deleted.
        /// Any pending webhook events for this subscription may still be dispatched.
        /// </summary>
        [HttpDelete("subscriptions/{id:guid}")]
        public async Task<ActionResult> DeleteSubscription(
            Guid id,
            CancellationToken ct = default)
        {
            if (!HasScope("clinical"))
                return ApiError(
                    "The 'clinical' scope is required for this endpoint. " +
                    "Upgrade to the Clinical Research tier at recipzee.com/api",
                    403);

            var apiKeyId = GetApiKeyId();
            var deleted = await _monitoring.DeleteSubscriptionAsync(apiKeyId, id, ct);

            if (!deleted)
                return ApiError("Monitoring subscription not found or does not belong to this API key.", 404);

            return SafetyOk(new { deleted = true, subscriptionId = id });
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private async Task<int> CountActiveSubscriptionsAsync(Guid apiKeyId, CancellationToken ct)
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Platerly.Api.Data.PlaterlyDbContext>();
            return await db.AllergenMonitoringSubscriptions
                .CountAsync(s => s.ApiKeyId == apiKeyId && s.IsActive, ct);
        }
    }
}
