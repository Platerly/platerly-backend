using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Monitoring
{
    /// <summary>
    /// Hosted background service that performs periodic allergen change detection
    /// for all active AllergenMonitoringSubscriptions.
    ///
    /// Every CheckInterval (default 60 min):
    ///   1. Load all active monitoring subscriptions.
    ///   2. For each subscription, re-fetch current allergen state for monitored ingredients.
    ///   3. Recompute the canonical snapshot hash.
    ///   4. If hash != stored SnapshotHash, compute the full diff and create a WebhookEvent
    ///      against the subscription's linked WebhookSubscription.
    ///   5. Update SnapshotHash, SnapshotJson, LastCheckedAt, and TotalChangesDetected.
    ///
    /// Webhook delivery (retry, HMAC signing, backoff) is handled by WebhookDispatchService.
    /// </summary>
    public class AllergenMonitoringBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AllergenMonitoringBackgroundService> _logger;

        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan StartupDelay  = TimeSpan.FromMinutes(5);

        public AllergenMonitoringBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<AllergenMonitoringBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AllergenMonitoringBackgroundService started. " +
                                   "First check in {Delay}.", StartupDelay);

            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunCheckCycleAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Allergen monitoring check cycle failed.");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }

        private async Task RunCheckCycleAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlaterlyDbContext>();

            var subscriptions = await db.AllergenMonitoringSubscriptions
                .Include(s => s.WebhookSubscription)
                .Where(s => s.IsActive && s.WebhookSubscription != null && s.WebhookSubscription.IsActive)
                .ToListAsync(ct);

            if (subscriptions.Count == 0) return;

            _logger.LogDebug("Allergen monitoring: checking {Count} subscription(s).", subscriptions.Count);

            int changesFound = 0;

            foreach (var sub in subscriptions)
            {
                try
                {
                    changesFound += await CheckSubscriptionAsync(db, sub, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to check monitoring subscription {Id}.", sub.Id);
                }
            }

            if (changesFound > 0)
                _logger.LogInformation(
                    "Allergen monitoring: {Changes} change event(s) queued across {Count} subscription(s).",
                    changesFound, subscriptions.Count);
        }

        private static async Task<int> CheckSubscriptionAsync(
            PlaterlyDbContext db,
            AllergenMonitoringSubscription sub,
            CancellationToken ct)
        {
            var ingredientNames = sub.IngredientNames
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(n => n.ToLower())
                .Distinct()
                .ToList();

            var allergenFilter = string.IsNullOrWhiteSpace(sub.AllergenTypesFilter)
                ? null
                : sub.AllergenTypesFilter
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

            // ── Fetch current state ──────────────────────────────────────────────

            var currentIngredients = await db.GlobalIngredients
                .AsNoTracking()
                .Include(gi => gi.Allergens)
                .Where(gi => gi.IsActive && ingredientNames.Contains(gi.NormalizedName))
                .ToListAsync(ct);

            // Also check aliases for any that didn't match normalised name directly
            var matchedNames = new HashSet<string>(
                currentIngredients.Select(gi => gi.NormalizedName),
                StringComparer.OrdinalIgnoreCase);
            var stillUnmatched = ingredientNames.Where(n => !matchedNames.Contains(n)).ToList();

            if (stillUnmatched.Any())
            {
                var aliasCandidates = await db.GlobalIngredients
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
                        if (stillUnmatched.Contains(alias) && !matchedNames.Contains(alias))
                        {
                            currentIngredients.Add(candidate);
                            matchedNames.Add(alias);
                        }
                    }
                }
            }

            // ── Compute current hash ─────────────────────────────────────────────

            var (currentJson, currentHash) = AllergenMonitoringService.ComputeSnapshot(
                currentIngredients, allergenFilter);

            sub.LastCheckedAt = DateTime.UtcNow;

            if (string.Equals(currentHash, sub.SnapshotHash, StringComparison.OrdinalIgnoreCase))
            {
                // No change
                await db.SaveChangesAsync(ct);
                return 0;
            }

            // ── Compute diff ─────────────────────────────────────────────────────

            var changes = ComputeDiff(sub.SnapshotJson, currentJson, allergenFilter);

            // ── Queue WebhookEvent ───────────────────────────────────────────────

            var payload = new AllergenChangeWebhookPayload
            {
                SubscriptionId = sub.Id,
                DetectedAt     = DateTime.UtcNow,
                Summary        = $"{changes.Count} ingredient(s) had allergen profile changes.",
                Changes        = changes
            };

            var payloadJson = JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { WriteIndented = false });

            db.WebhookEvents.Add(new WebhookEvent
            {
                SubscriptionId = sub.WebhookSubscriptionId,
                EventType      = "allergen.change",
                Payload        = payloadJson,
                Status         = WebhookEventStatus.Pending,
                NextRetryAt    = DateTime.UtcNow
            });

            // ── Update snapshot ──────────────────────────────────────────────────

            sub.SnapshotHash              = currentHash;
            sub.SnapshotJson              = currentJson;
            sub.LastChangeDetectedAt      = DateTime.UtcNow;
            sub.TotalChangesDetected     += 1;

            await db.SaveChangesAsync(ct);
            return 1;
        }

        // ── Diff computation ───────────────────────────────────────────────────

        private static List<IngredientAllergenChange> ComputeDiff(
            string previousJson,
            string currentJson,
            List<string>? allergenFilter)
        {
            var filterSet = allergenFilter != null
                ? new HashSet<string>(allergenFilter, StringComparer.OrdinalIgnoreCase)
                : null;

            // Deserialise both snapshots
            var previous = DeserialiseSnapshot(previousJson);
            var current  = DeserialiseSnapshot(currentJson);

            var allNames = previous.Keys.Union(current.Keys, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var changes = new List<IngredientAllergenChange>();

            foreach (var name in allNames)
            {
                previous.TryGetValue(name, out var prevAllergens);
                current.TryGetValue(name, out var currAllergens);

                prevAllergens ??= new List<AllergenSnapshot>();
                currAllergens ??= new List<AllergenSnapshot>();

                // Apply filter
                if (filterSet != null && filterSet.Count > 0)
                {
                    prevAllergens = prevAllergens.Where(a => filterSet.Contains(a.AllergenType)).ToList();
                    currAllergens = currAllergens.Where(a => filterSet.Contains(a.AllergenType)).ToList();
                }

                var added   = currAllergens.Where(c => !prevAllergens.Any(p =>
                    string.Equals(p.AllergenType, c.AllergenType, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var removed = prevAllergens.Where(p => !currAllergens.Any(c =>
                    string.Equals(c.AllergenType, p.AllergenType, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var modified = currAllergens.Where(c =>
                {
                    var prev = prevAllergens.FirstOrDefault(p =>
                        string.Equals(p.AllergenType, c.AllergenType, StringComparison.OrdinalIgnoreCase));
                    return prev != null &&
                           (prev.MayContain != c.MayContain || prev.CrossContactRisk != c.CrossContactRisk);
                }).ToList();

                if (!added.Any() && !removed.Any() && !modified.Any()) continue;

                changes.Add(new IngredientAllergenChange
                {
                    IngredientName   = name,
                    PreviousAllergens = prevAllergens,
                    CurrentAllergens  = currAllergens,
                    AddedAllergens    = added,
                    RemovedAllergens  = removed,
                    ModifiedAllergens = modified
                });
            }

            return changes;
        }

        private static Dictionary<string, List<AllergenSnapshot>> DeserialiseSnapshot(string json)
        {
            var result = new Dictionary<string, List<AllergenSnapshot>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString() ?? string.Empty;
                    var allergens = new List<AllergenSnapshot>();

                    if (item.TryGetProperty("allergens", out var arr))
                    {
                        foreach (var a in arr.EnumerateArray())
                        {
                            allergens.Add(new AllergenSnapshot
                            {
                                AllergenType     = a.GetProperty("type").GetString() ?? string.Empty,
                                MayContain       = a.TryGetProperty("mayContain", out var mc) && mc.GetBoolean(),
                                CrossContactRisk = a.TryGetProperty("crossContact", out var cc) && cc.GetBoolean()
                            });
                        }
                    }

                    result[name] = allergens;
                }
            }
            catch
            {
                // Malformed snapshot — treat as empty (will trigger full diff)
            }
            return result;
        }
    }
}
