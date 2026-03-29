namespace Platerly.Api.DTOs
{
    // ── Register ───────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/b2b/monitoring/register
    /// Register a list of ingredients for allergen change monitoring.
    /// When any ingredient's allergen profile changes, a webhook fires to the
    /// supplied URL with a full diff of what changed.
    /// </summary>
    public class MonitoringRegisterRequest
    {
        /// <summary>
        /// Ingredient names to monitor (matched against GlobalIngredients.NormalizedName
        /// and aliases). At least 1, maximum 100.
        /// </summary>
        public List<string> Ingredients { get; set; } = new();

        /// <summary>
        /// HTTPS URL to deliver webhook notifications to.
        /// Must respond 2xx within 10 seconds to be considered delivered.
        /// </summary>
        public string WebhookUrl { get; set; } = string.Empty;

        /// <summary>
        /// Optional allergen type filter. When provided, notifications are only
        /// sent when changes involve these allergen types.
        /// Accepted values: Peanuts, Tree Nuts, Milk, Eggs, Gluten, Soy,
        /// Fish, Shellfish, Sesame, Sulfites, Mustard (and any type in the DB).
        /// Null/empty = notify on any allergen change.
        /// </summary>
        public List<string>? AllergenTypesFilter { get; set; }
    }

    /// <summary>
    /// Returned once on registration. The webhookSecret is shown ONLY at registration
    /// time — it cannot be retrieved again. Store it securely to verify webhook signatures.
    /// </summary>
    public class MonitoringRegisterResponse
    {
        public Guid SubscriptionId { get; set; }

        /// <summary>
        /// HMAC-SHA256 signing secret. Verify incoming webhooks by computing:
        ///   HMAC-SHA256(secret, requestBody) == X-Recipzee-Signature header value.
        /// Shown ONLY at registration — store immediately.
        /// </summary>
        public string WebhookSecret { get; set; } = string.Empty;

        public string WebhookUrl { get; set; } = string.Empty;
        public List<string> MonitoredIngredients { get; set; } = new();
        public List<string> UnmatchedIngredients { get; set; } = new();
        public List<string>? AllergenTypesFilter { get; set; }
        public int InitialAllergenCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ── List ──────────────────────────────────────────────────────────────────

    public class MonitoringSubscriptionDto
    {
        public Guid SubscriptionId { get; set; }
        public string WebhookUrl { get; set; } = string.Empty;
        public List<string> MonitoredIngredients { get; set; } = new();
        public List<string>? AllergenTypesFilter { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastCheckedAt { get; set; }
        public DateTime? LastChangeDetectedAt { get; set; }
        public int TotalChangesDetected { get; set; }
    }

    public class MonitoringListResponse
    {
        public List<MonitoringSubscriptionDto> Subscriptions { get; set; } = new();
        public int TotalCount { get; set; }
    }

    // ── Webhook payload ───────────────────────────────────────────────────────
    // (Serialised and stored in WebhookEvent.Payload — not returned from the API)

    public class AllergenChangeWebhookPayload
    {
        public string EventType { get; set; } = "allergen.change";
        public Guid SubscriptionId { get; set; }
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
        public string Summary { get; set; } = string.Empty;
        public List<IngredientAllergenChange> Changes { get; set; } = new();
    }

    public class IngredientAllergenChange
    {
        public string IngredientName { get; set; } = string.Empty;

        /// <summary>Allergens present in the previous snapshot.</summary>
        public List<AllergenSnapshot> PreviousAllergens { get; set; } = new();

        /// <summary>Allergens present in the current state.</summary>
        public List<AllergenSnapshot> CurrentAllergens { get; set; } = new();

        /// <summary>Allergens added since the last check.</summary>
        public List<AllergenSnapshot> AddedAllergens { get; set; } = new();

        /// <summary>Allergens removed since the last check.</summary>
        public List<AllergenSnapshot> RemovedAllergens { get; set; } = new();

        /// <summary>Allergens whose flags changed (MayContain or CrossContactRisk).</summary>
        public List<AllergenSnapshot> ModifiedAllergens { get; set; } = new();
    }

    public class AllergenSnapshot
    {
        public string AllergenType { get; set; } = string.Empty;
        public bool MayContain { get; set; }
        public bool CrossContactRisk { get; set; }
    }
}
