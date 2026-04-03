using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Monitoring
{
    /// <summary>
    /// B2B scoped service — CRUD for <c>AllergenMonitoringSubscriptions</c>.
    /// Depends on <see cref="PlaterlyDbContext"/>.
    ///
    /// Responsibilities:
    /// <list type="bullet">
    ///   <item><see cref="RegisterAsync"/>: resolves ingredient names (direct NormalizedName match
    ///     then alias fallback), computes an initial SHA-256 snapshot, creates a linked
    ///     <c>WebhookSubscription</c> (event type <c>"allergen.change"</c>) and a
    ///     <c>AllergenMonitoringSubscription</c> row, and returns a 32-byte random base64 webhook
    ///     secret to the caller.</item>
    ///   <item><see cref="ListSubscriptionsAsync"/>: returns all active subscriptions for an API key.</item>
    ///   <item><see cref="DeleteSubscriptionAsync"/>: soft-deletes the monitoring subscription and
    ///     its linked <c>WebhookSubscription</c>.</item>
    ///   <item><see cref="ComputeSnapshot"/>: static helper shared with
    ///     <c>AllergenMonitoringBackgroundService</c> — serialises ingredient allergen state to
    ///     canonical JSON and returns a hex SHA-256 hash for change detection.</item>
    /// </list>
    /// Change detection and webhook dispatch are handled by <c>AllergenMonitoringBackgroundService</c>.
    /// </summary>
    public class AllergenMonitoringService : IAllergenMonitoringService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<AllergenMonitoringService> _logger;

        public AllergenMonitoringService(
            PlaterlyDbContext db,
            ILogger<AllergenMonitoringService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Registers a new allergen monitoring subscription for the given API key.
        /// Resolves ingredient names against <c>GlobalIngredients.NormalizedName</c>, with alias
        /// fallback for unmatched names. Computes an initial SHA-256 snapshot of the current allergen
        /// state. Creates a <c>WebhookSubscription</c> (event type <c>"allergen.change"</c>) with a
        /// 32-byte random base64 secret, then persists the <c>AllergenMonitoringSubscription</c> row.
        /// The webhook secret is returned to the caller only at registration time.
        /// </summary>
        /// <param name="apiKeyId">Authenticated API key ID owning this subscription.</param>
        /// <param name="request">Ingredient list, optional allergen-type filter, and webhook URL.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Registration response including subscription ID, webhook secret, and unmatched ingredients.</returns>
        public async Task<MonitoringRegisterResponse> RegisterAsync(
            Guid apiKeyId,
            MonitoringRegisterRequest request,
            CancellationToken ct = default)
        {
            // ── Normalise ingredient names ───────────────────────────────────────

            var submittedNames = request.Ingredients
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var normalised = submittedNames.Select(n => n.ToLower()).ToList();

            // ── Resolve ingredients (direct + alias) ─────────────────────────────

            var directMatches = await _db.GlobalIngredients
                .AsNoTracking()
                .Include(gi => gi.Allergens)
                .Where(gi => gi.IsActive && normalised.Contains(gi.NormalizedName))
                .ToListAsync(ct);

            var matchedNormalised = new HashSet<string>(
                directMatches.Select(gi => gi.NormalizedName),
                StringComparer.OrdinalIgnoreCase);

            // Alias fallback for anything not yet matched
            var unmatchedNormalised = normalised.Where(n => !matchedNormalised.Contains(n)).ToList();
            if (unmatchedNormalised.Any())
            {
                var aliasCandidates = await _db.GlobalIngredients
                    .AsNoTracking()
                    .Include(gi => gi.Allergens)
                    .Where(gi => gi.IsActive && gi.Aliases != null)
                    .ToListAsync(ct);

                foreach (var candidate in aliasCandidates)
                {
                    if (candidate.Aliases == null) continue;
                    var aliases = candidate.Aliases
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(a => a.ToLower());
                    foreach (var alias in aliases)
                    {
                        if (unmatchedNormalised.Contains(alias) && !matchedNormalised.Contains(alias))
                        {
                            directMatches.Add(candidate);
                            matchedNormalised.Add(alias);
                        }
                    }
                }
            }

            var unmatchedNames = submittedNames
                .Where(n => !matchedNormalised.Contains(n.ToLower()))
                .ToList();

            // ── Compute initial allergen snapshot ────────────────────────────────

            var allergenFilter = request.AllergenTypesFilter?
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .ToList();

            var (snapshotJson, snapshotHash) = ComputeSnapshot(directMatches, allergenFilter);

            int initialAllergenCount = directMatches
                .SelectMany(gi => gi.Allergens)
                .Count(a => allergenFilter == null || allergenFilter.Count == 0 ||
                            allergenFilter.Contains(a.AllergenType, StringComparer.OrdinalIgnoreCase));

            // ── Create WebhookSubscription for delivery ──────────────────────────

            var secretBytes = RandomNumberGenerator.GetBytes(32);
            var secret = Convert.ToBase64String(secretBytes);

            var webhookSub = new WebhookSubscription
            {
                ApiKeyId = apiKeyId,
                Url = request.WebhookUrl,
                Secret = secret,
                EventTypes = "allergen.change",
                IsActive = true
            };
            _db.WebhookSubscriptions.Add(webhookSub);

            // ── Create monitoring subscription ───────────────────────────────────

            var monitoringSub = new AllergenMonitoringSubscription
            {
                ApiKeyId = apiKeyId,
                WebhookSubscriptionId = webhookSub.Id,
                IngredientNames = string.Join(",", submittedNames),
                AllergenTypesFilter = allergenFilter?.Any() == true
                    ? string.Join(",", allergenFilter)
                    : null,
                SnapshotHash = snapshotHash,
                SnapshotJson = snapshotJson,
                IsActive = true
            };
            _db.AllergenMonitoringSubscriptions.Add(monitoringSub);

            await _db.SaveChangesAsync(ct);

            return new MonitoringRegisterResponse
            {
                SubscriptionId = monitoringSub.Id,
                WebhookSecret = secret,
                WebhookUrl = request.WebhookUrl,
                MonitoredIngredients = submittedNames,
                UnmatchedIngredients = unmatchedNames,
                AllergenTypesFilter = allergenFilter?.Any() == true ? allergenFilter : null,
                InitialAllergenCount = initialAllergenCount,
                CreatedAt = monitoringSub.CreatedAt
            };
        }

        /// <summary>
        /// Returns all active monitoring subscriptions for the given API key, ordered by creation date descending.
        /// </summary>
        /// <param name="apiKeyId">Authenticated API key ID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>List response with subscription summaries and total count.</returns>
        public async Task<MonitoringListResponse> ListSubscriptionsAsync(
            Guid apiKeyId,
            CancellationToken ct = default)
        {
            var subs = await _db.AllergenMonitoringSubscriptions
                .AsNoTracking()
                .Include(s => s.WebhookSubscription)
                .Where(s => s.ApiKeyId == apiKeyId && s.IsActive)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync(ct);

            var dtos = subs.Select(s => new MonitoringSubscriptionDto
            {
                SubscriptionId = s.Id,
                WebhookUrl = s.WebhookSubscription?.Url ?? string.Empty,
                MonitoredIngredients = s.IngredientNames
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList(),
                AllergenTypesFilter = string.IsNullOrWhiteSpace(s.AllergenTypesFilter)
                    ? null
                    : s.AllergenTypesFilter
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList(),
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt,
                LastCheckedAt = s.LastCheckedAt,
                LastChangeDetectedAt = s.LastChangeDetectedAt,
                TotalChangesDetected = s.TotalChangesDetected
            }).ToList();

            return new MonitoringListResponse
            {
                Subscriptions = dtos,
                TotalCount = dtos.Count
            };
        }

        /// <summary>
        /// Deletes an allergen monitoring subscription and its linked <c>WebhookSubscription</c>.
        /// Returns <c>false</c> when the subscription does not exist or does not belong to the caller.
        /// </summary>
        /// <param name="apiKeyId">Authenticated API key ID — enforces ownership.</param>
        /// <param name="subscriptionId">Subscription to delete.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> if deleted; <c>false</c> if not found.</returns>
        public async Task<bool> DeleteSubscriptionAsync(
            Guid apiKeyId,
            Guid subscriptionId,
            CancellationToken ct = default)
        {
            var sub = await _db.AllergenMonitoringSubscriptions
                .Include(s => s.WebhookSubscription)
                .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.ApiKeyId == apiKeyId, ct);

            if (sub == null) return false;

            // Remove the linked webhook subscription too (cascades cleanly)
            if (sub.WebhookSubscription != null)
                _db.WebhookSubscriptions.Remove(sub.WebhookSubscription);

            _db.AllergenMonitoringSubscriptions.Remove(sub);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // ── Snapshot helpers (shared with background service) ──────────────────

        /// <summary>
        /// Builds a canonically-sorted JSON snapshot and its SHA-256 hash
        /// for a list of matched GlobalIngredients.
        /// </summary>
        public static (string json, string hash) ComputeSnapshot(
            IEnumerable<GlobalIngredient> ingredients,
            IEnumerable<string>? allergenFilter)
        {
            var filterSet = allergenFilter != null
                ? new HashSet<string>(allergenFilter, StringComparer.OrdinalIgnoreCase)
                : null;

            var snapshot = ingredients
                .OrderBy(gi => gi.NormalizedName)
                .Select(gi => new
                {
                    name = gi.NormalizedName,
                    allergens = gi.Allergens
                        .Where(a => filterSet == null || filterSet.Count == 0 || filterSet.Contains(a.AllergenType))
                        .OrderBy(a => a.AllergenType)
                        .Select(a => new
                        {
                            type           = a.AllergenType,
                            mayContain     = a.MayContain,
                            crossContact   = a.CrossContactRisk
                        })
                        .ToArray()
                })
                .ToArray();

            var json = JsonSerializer.Serialize(snapshot,
                new JsonSerializerOptions { WriteIndented = false });
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            return (json, hash);
        }
    }
}
