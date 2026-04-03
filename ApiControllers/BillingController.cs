using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Contracts.Billing;
using Platerly.Api.Data;
using Stripe;
using Stripe.Checkout;

namespace Platerly.Api.Controllers;

using UserEntity = Platerly.Api.Models.User;

/// <summary>
/// B2C Billing controller. Manages Stripe subscriptions and billing status for Recipzee users.
///
/// Base route: /api/billing
/// Auth requirement: [Authorize] (JWT) on all endpoints except the Stripe webhook.
///
/// Responsibilities:
///   • Create Stripe Checkout Sessions for upgrading to Premium.
///   • Handle incoming Stripe webhook events and mirror subscription state to the DB:
///       - checkout.session.completed    → links customer/subscription, activates IsPremium
///       - customer.subscription.created → mirrors subscription fields to user row
///       - customer.subscription.updated → updates status, cancel_at_period_end, period end date
///       - customer.subscription.deleted → deactivates IsPremium when subscription ends
///   • Create Stripe Customer Portal Sessions for self-serve plan management.
///   • Return billing status including trial state for the Settings UI.
///   • Cancel and resume subscriptions (sets/clears cancel_at_period_end via Stripe API).
///
/// The <c>MirrorSubscriptionToUser</c> helper normalises subscription fields from Stripe.NET
/// (which has changed property types across versions) into the <see cref="UserEntity"/> model.
/// </summary>
[ApiController]
[Route("api/billing")]
[Authorize]
public class BillingController : ControllerBase
{
    private readonly IConfiguration _cfg;
    private readonly PlaterlyDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<BillingController> _log;
    private readonly BillingEmailOrchestrator _orchestrator;

    public BillingController(IConfiguration cfg, PlaterlyDbContext db, IWebHostEnvironment env, ILogger<BillingController> log, BillingEmailOrchestrator orchestrator)
    {
        _cfg = cfg;
        _db = db;
        _env = env;
        _log = log;
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Gets the authenticated user's ID from JWT claims.
    /// </summary>
    private Guid? GetAuthenticatedUserId()
    {
        // ASP.NET Core maps JWT "sub" claim to ClaimTypes.NameIdentifier
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("nameid")?.Value;
        return Guid.TryParse(userIdClaim, out var uid) ? uid : null;
    }

    /// <summary>
    /// Validates that the requested userId matches the authenticated user.
    /// </summary>
    private bool ValidateUserAccess(string requestedUserId, out Guid userId)
    {
        var authUserId = GetAuthenticatedUserId();
        if (authUserId == null || !Guid.TryParse(requestedUserId, out userId))
        {
            userId = Guid.Empty;
            return false;
        }
        return userId == authUserId.Value;
    }

    private void EnsureStripeApiKey()
    {
        var key = _cfg["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Stripe:SecretKey is not configured.");

        StripeConfiguration.ApiKey = key;
    }

    private string GetPublicDomain()
    {
        return _env.IsDevelopment()
            ? (_cfg["Stripe:PublicDomainDev"] ?? "http://localhost:5173")
            : (_cfg["Stripe:PublicDomainProd"] ?? "https://recipzee.com");
    }

    private async Task<string> EnsureValidCustomerIdAsync(UserEntity user)
    {
        var custSvc = new CustomerService();

        if (!string.IsNullOrWhiteSpace(user.StripeCustomerId))
        {
            try
            {
                var existing = await custSvc.GetAsync(user.StripeCustomerId);

                // best-effort email sync
                if (!string.Equals(existing.Email, user.Email, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await custSvc.UpdateAsync(existing.Id, new CustomerUpdateOptions { Email = user.Email });
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Unable to sync Stripe customer email");
                    }
                }

                return existing.Id;
            }
            catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
            {
                _log.LogWarning(
                    "Stripe customer {CustomerId} not found in current mode. Creating a new customer.",
                    user.StripeCustomerId
                );
            }
        }

        var created = await custSvc.CreateAsync(new CustomerCreateOptions
        {
            Email = user.Email,
            Metadata = new Dictionary<string, string>
            {
                ["pl_user_id"] = user.Id.ToString()
            }
        });

        user.StripeCustomerId = created.Id;
        await _db.SaveChangesAsync();

        return created.Id;
    }

    // -----------------------------
    // Helpers (Stripe.NET-version safe)
    // -----------------------------

    /// <summary>
    /// Reads a string property from an object by name using reflection.
    /// Used to handle differences in Stripe.NET property casing across SDK versions.
    /// </summary>
    private static string? TryGetStringProperty(object obj, string propName)
    {
        var p = obj.GetType().GetProperty(propName);
        if (p == null) return null;
        return p.GetValue(obj) as string;
    }

    /// <summary>
    /// Reads a property value from an object by name using reflection.
    /// Used to access Stripe object fields that may be named differently across SDK versions.
    /// </summary>
    private static object? TryGetProperty(object obj, string propName)
    {
        var p = obj.GetType().GetProperty(propName);
        return p?.GetValue(obj);
    }

    /// <summary>
    /// Extracts a Stripe resource ID string from various forms the Stripe.NET SDK may provide:
    /// a plain string, a <see cref="Customer"/>, a <see cref="Subscription"/>,
    /// an <c>ExpandableField&lt;T&gt;</c> with an <c>Id</c> property, or an expanded object
    /// reachable through a <c>Value</c> property. Returns null when no ID can be resolved.
    /// </summary>
    private static string? ExtractStripeId(object? value)
    {
        if (value == null) return null;

        // direct string
        if (value is string s) return string.IsNullOrWhiteSpace(s) ? null : s;

        // common Stripe objects
        if (value is Customer c) return c.Id;
        if (value is Subscription sub) return sub.Id;

        // ExpandableField<T> variants
        var t = value.GetType();

        // Try "Id" property
        var idProp = t.GetProperty("Id");
        if (idProp?.PropertyType == typeof(string))
        {
            var idVal = idProp.GetValue(value) as string;
            if (!string.IsNullOrWhiteSpace(idVal)) return idVal;
        }

        // Try "Value" property (ExpandableField<T>.Value)
        var valueProp = t.GetProperty("Value");
        var inner = valueProp?.GetValue(value);
        if (inner != null)
        {
            var innerIdProp = inner.GetType().GetProperty("Id");
            var innerId = innerIdProp?.GetValue(inner) as string;
            if (!string.IsNullOrWhiteSpace(innerId)) return innerId;
        }

        return null;
    }

    private static string? GetSessionCustomerIdSafe(Session session)
    {
        var customerId = TryGetStringProperty(session, "CustomerId");
        if (!string.IsNullOrWhiteSpace(customerId)) return customerId;

        var customerObj = TryGetProperty(session, "Customer");
        return ExtractStripeId(customerObj);
    }

    private static string? GetSessionSubscriptionIdSafe(Session session)
    {
        var subId = TryGetStringProperty(session, "SubscriptionId");
        if (!string.IsNullOrWhiteSpace(subId)) return subId;

        var subObj = TryGetProperty(session, "Subscription");
        return ExtractStripeId(subObj);
    }

    private static string? GetSubscriptionCustomerIdSafe(Subscription sub)
    {
        var customerId = TryGetStringProperty(sub, "CustomerId");
        if (!string.IsNullOrWhiteSpace(customerId)) return customerId;

        var customerObj = TryGetProperty(sub, "Customer");
        return ExtractStripeId(customerObj);
    }

    /// <summary>
    /// Reads <c>CurrentPeriodEnd</c> from a subscription in a version-safe way.
    /// Stripe.NET has exposed this field as DateTime, DateTimeOffset, long (Unix), or int (Unix)
    /// across different SDK versions. Falls back to the first non-null
    /// <c>Items.Data[n].CurrentPeriodEnd</c> when the top-level property is absent.
    /// </summary>
    private static DateTimeOffset? GetCurrentPeriodEndSafe(Subscription sub)
    {
        // Some Stripe.NET versions expose CurrentPeriodEnd directly (DateTime/DateTimeOffset/unix)
        var prop = sub.GetType().GetProperty("CurrentPeriodEnd");
        if (prop != null)
        {
            var val = prop.GetValue(sub);
            if (val is DateTime dt) return new DateTimeOffset(dt);
            if (val is DateTimeOffset dto) return dto;
            if (val is long l) return DateTimeOffset.FromUnixTimeSeconds(l);
            if (val is int i) return DateTimeOffset.FromUnixTimeSeconds(i);
        }

        // Fallback: item-level CurrentPeriodEnd (your older Stripe.NET behavior)
        var itemEnd = sub.Items?.Data?
            .Select(i => i.CurrentPeriodEnd)
            .Where(d => d != null)
            .OrderBy(d => d)
            .FirstOrDefault();

        return itemEnd is null ? null : new DateTimeOffset(itemEnd.Value);
    }

    /// <summary>
    /// Reads <c>CancelAt</c> from a subscription in a version-safe way, handling the same
    /// type variety as <see cref="GetCurrentPeriodEndSafe"/>. The Stripe Customer Portal can
    /// set <c>cancel_at</c> (a future timestamp) independently of <c>cancel_at_period_end</c>.
    /// </summary>
    private static DateTimeOffset? GetCancelAtSafe(Subscription sub)
    {
        // Stripe portal may set cancel_at without setting cancel_at_period_end
        var prop = sub.GetType().GetProperty("CancelAt");
        if (prop != null)
        {
            var val = prop.GetValue(sub);
            if (val is DateTime dt) return new DateTimeOffset(dt);
            if (val is DateTimeOffset dto) return dto;
            if (val is long l) return DateTimeOffset.FromUnixTimeSeconds(l);
            if (val is int i) return DateTimeOffset.FromUnixTimeSeconds(i);
        }

        return null;
    }

    /// <summary>
    /// Applies the latest Stripe subscription state to the <see cref="UserEntity"/> in the DB.
    /// Called by all three webhook event handlers and the live-sync in <see cref="GetStatus"/>.
    ///
    /// DB fields updated:
    ///   <c>StripeSubscriptionId</c>, <c>PremiumStatus</c> (raw Stripe status string),
    ///   <c>IsPremium</c> (true when status is "active" or "trialing"),
    ///   <c>PremiumCancelAtPeriodEnd</c> (true when <c>cancel_at_period_end</c> OR a future
    ///   <c>cancel_at</c> timestamp is set — the Stripe portal may use either),
    ///   <c>PremiumCurrentPeriodEnd</c> (used by the UI for "Renews on / Ends on" display),
    ///   <c>PremiumSince</c> (set once when premium first activates; cleared on deactivation).
    ///
    /// Caller is responsible for calling <c>_db.SaveChangesAsync()</c> after this method.
    /// </summary>
    private void MirrorSubscriptionToUser(UserEntity user, Subscription sub)
    {
        var cancelAt = GetCancelAtSafe(sub);

        user.StripeSubscriptionId = sub.Id;
        user.PremiumStatus = sub.Status;

        // Still premium if active/trialing, even if set to cancel later
        user.IsPremium = sub.Status is "active" or "trialing";

        // ✅ IMPORTANT FIX:
        // Stripe portal sometimes uses cancel_at (timestamp) without setting cancel_at_period_end
        user.PremiumCancelAtPeriodEnd = sub.CancelAtPeriodEnd || cancelAt != null;

        // For UI: "Renews on / Ends on" uses period end
        user.PremiumCurrentPeriodEnd = GetCurrentPeriodEndSafe(sub);

        if (user.IsPremium)
            user.PremiumSince ??= DateTimeOffset.UtcNow;
        else
            user.PremiumSince = null;
    }

    /// <summary>
    /// Creates a Stripe Checkout Session for subscribing to Recipzee Premium.
    ///
    /// Ensures or creates a Stripe Customer for the user, then builds a hosted Checkout page
    /// in "subscription" mode for the configured <c>Stripe:PriceIdPremium</c>.
    /// Embeds <c>pl_user_id</c> in both session metadata and subscription metadata so that
    /// the <c>checkout.session.completed</c> webhook can map back to the correct user.
    ///
    /// Idempotency key format: <c>chk:{userId}:{yyyyMMddHHmm}</c> — safe to retry within
    /// the same minute without creating duplicate sessions.
    /// </summary>
    /// <param name="req">Request body containing <c>UserId</c> (must match authenticated user).</param>
    /// <returns>
    /// 200 with <c>{ url }</c> — the hosted Checkout URL to redirect the user to;
    /// 403 if <c>UserId</c> does not match the JWT subject;
    /// 500 on Stripe API errors.
    /// </returns>
    [HttpPost("create-checkout-session")]
    [Authorize]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateCheckoutRequest req)
    {
        try
        {
            EnsureStripeApiKey();

            if (!ValidateUserAccess(req.UserId, out var uid))
                return Forbid();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
            if (user is null) return BadRequest("User not found");

            var priceId = _cfg["Stripe:PriceIdPremium"];
            if (string.IsNullOrWhiteSpace(priceId))
                return StatusCode(500, "Stripe:PriceIdPremium is not configured.");

            var domain = GetPublicDomain();
            var successUrl = $"{domain}/settings?tab=subscription&sub=success";
            var cancelUrl = $"{domain}/settings?tab=subscription&sub=cancel";

            var customerId = await EnsureValidCustomerIdAsync(user);

            var allowPromo = bool.TryParse(_cfg["Stripe:AllowPromoCodes"], out var apc) && apc;
            var enableTax = bool.TryParse(_cfg["Stripe:EnableAutomaticTax"], out var tax) && tax;

            var options = new SessionCreateOptions
            {
                Mode = "subscription",
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                Customer = customerId,

                PaymentMethodTypes = new List<string> { "card" },

                LineItems = new List<SessionLineItemOptions>
                {
                    new() { Price = priceId, Quantity = 1 }
                },

                AllowPromotionCodes = allowPromo,

                // Helps map webhooks back to user
                ClientReferenceId = user.Id.ToString(),

                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string>
                    {
                        ["pl_user_id"] = user.Id.ToString()
                    }
                },

                Metadata = new Dictionary<string, string>
                {
                    ["pl_user_id"] = user.Id.ToString()
                },

                BillingAddressCollection = enableTax ? "required" : "auto",
                CustomerUpdate = enableTax
                    ? new SessionCustomerUpdateOptions { Address = "auto", Name = "auto" }
                    : null,

                Locale = "auto"
            };

            if (enableTax)
                options.AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true };

            var session = await new SessionService().CreateAsync(
                options,
                new RequestOptions
                {
                    IdempotencyKey = $"chk:{user.Id}:{DateTime.UtcNow:yyyyMMddHHmm}"
                }
            );

            _log.LogInformation("Created checkout session for {UserId} -> {Url}", user.Id, session.Url);
            return Ok(new { url = session.Url });
        }
        catch (StripeException ex)
        {
            _log.LogError(ex,
                "Stripe error creating checkout session. Code={Code} Msg={Msg}",
                ex.StripeError?.Code, ex.StripeError?.Message);

            return StatusCode(500, $"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error creating checkout session");
            return StatusCode(500, "Could not start checkout.");
        }
    }

    /// <summary>
    /// Receives and processes Stripe webhook events. This endpoint is public and unauthenticated;
    /// authenticity is verified using the HMAC-SHA256 Stripe-Signature header against
    /// <c>Stripe:WebhookSecret</c> (tolerance: 300 seconds to allow for clock skew).
    ///
    /// Handled event types:
    /// <list type="bullet">
    ///   <item>
    ///     <c>checkout.session.completed</c> — Links the Stripe Customer and Subscription IDs to
    ///     the user row (identified via <c>client_reference_id</c> or <c>pl_user_id</c> metadata),
    ///     calls <see cref="MirrorSubscriptionToUser"/> to activate premium, and sends a
    ///     "Welcome to Premium" email (idempotent via <see cref="BillingEmailOrchestrator"/>).
    ///   </item>
    ///   <item>
    ///     <c>customer.subscription.created</c> — Mirrors the new subscription fields to the user row.
    ///   </item>
    ///   <item>
    ///     <c>customer.subscription.updated</c> — Updates status, cancel flags, and period end date.
    ///     Covers plan changes, trial conversions, and portal-initiated cancellation scheduling.
    ///   </item>
    ///   <item>
    ///     <c>customer.subscription.deleted</c> — Sets <c>IsPremium = false</c> and clears
    ///     <c>PremiumSince</c> when the subscription reaches its final deleted state.
    ///   </item>
    /// </list>
    ///
    /// All other event types are silently ignored (returns 200 to acknowledge receipt).
    /// Signature or parse failures return 400 so Stripe will retry delivery.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var sigHeader = Request.Headers["Stripe-Signature"].ToString();

        var webhookSecret = _cfg["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
            return StatusCode(500, "Stripe:WebhookSecret is not configured.");

        EnsureStripeApiKey();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                sigHeader,
                webhookSecret,
                tolerance: 300,
                throwOnApiVersionMismatch: false
            );
        }
        catch (StripeException ex)
        {
            _log.LogWarning(ex, "Stripe webhook construct failed (signature/parse/version mismatch).");
            return BadRequest();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Stripe webhook construct failed (unexpected).");
            return BadRequest();
        }

        try
        {
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutSessionCompleted(stripeEvent);
                    break;

                case "customer.subscription.created":
                case "customer.subscription.updated":
                case "customer.subscription.deleted":
                    await HandleSubscriptionChanged(stripeEvent);
                    break;

                default:
                    // ignore others
                    break;
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Stripe webhook handler error for {EventType}", stripeEvent.Type);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Handles the <c>checkout.session.completed</c> event.
    /// Resolves the user via <c>ClientReferenceId</c> (preferred) or <c>pl_user_id</c> session metadata,
    /// stores the Stripe Customer ID, fetches the full Subscription object to call
    /// <see cref="MirrorSubscriptionToUser"/>, and triggers the welcome email once via
    /// <see cref="BillingEmailOrchestrator.TrySendOnceAsync"/> (idempotent on webhook retries).
    /// </summary>
    private async Task HandleCheckoutSessionCompleted(Event stripeEvent)
    {
        var session = stripeEvent.Data.Object as Session;
        if (session == null) return;

        // Prefer client_reference_id; fallback metadata
        var plUserId = session.ClientReferenceId;

        if (string.IsNullOrWhiteSpace(plUserId) &&
            session.Metadata != null &&
            session.Metadata.TryGetValue("pl_user_id", out var mid))
        {
            plUserId = mid;
        }

        if (!Guid.TryParse(plUserId, out var uid))
        {
            _log.LogWarning("checkout.session.completed missing/invalid pl_user_id mapping.");
            return;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
        if (user == null)
        {
            _log.LogWarning("No user found for pl_user_id={PlUserId}", plUserId);
            return;
        }

        var customerId = GetSessionCustomerIdSafe(session);
        var subId = GetSessionSubscriptionIdSafe(session);

        if (!string.IsNullOrWhiteSpace(customerId))
            user.StripeCustomerId = customerId;

        if (!string.IsNullOrWhiteSpace(subId))
        {
            var subService = new SubscriptionService();
            var sub = await subService.GetAsync(subId);
            MirrorSubscriptionToUser(user, sub);
        }
        else
        {
            _log.LogWarning("checkout.session.completed had no subscription id on session.");
        }

        await _db.SaveChangesAsync();

        // Send welcome to premium email (idempotent — safe on webhook retries)
        var emailAddress = user.BillingEmail ?? user.Email;
        if (!string.IsNullOrWhiteSpace(emailAddress))
        {
            var frontendUrl = _cfg["FrontendBaseUrl"] ?? "https://www.recipzee.com";
            await _orchestrator.TrySendOnceAsync(
                user.Id,
                emailAddress,
                stripeEvent.Id,
                "checkout.welcome_premium",
                "Welcome to Recipzee Premium!",
                TrialEmailService.BuildWelcomePremiumEmail(user.DisplayName ?? "there", frontendUrl));
        }
    }

    /// <summary>
    /// Handles <c>customer.subscription.created</c>, <c>customer.subscription.updated</c>,
    /// and <c>customer.subscription.deleted</c> events.
    /// Resolves the user by <c>StripeCustomerId</c> and calls <see cref="MirrorSubscriptionToUser"/>
    /// to keep the DB in sync with the latest subscription state from Stripe.
    /// </summary>
    private async Task HandleSubscriptionChanged(Event stripeEvent)
    {
        var sub = stripeEvent.Data.Object as Subscription;
        if (sub == null) return;

        var customerId = GetSubscriptionCustomerIdSafe(sub);
        if (string.IsNullOrWhiteSpace(customerId))
            return;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);
        if (user == null)
        {
            _log.LogWarning("No user found for StripeCustomerId={CustomerId}", customerId);
            return;
        }

        MirrorSubscriptionToUser(user, sub);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a Stripe Customer Portal Session so the user can self-manage their subscription
    /// (upgrade, downgrade, cancel, update payment method).
    /// The return URL appends <c>?portal=return</c> so the frontend knows to re-fetch billing status.
    /// </summary>
    /// <param name="req">Request body containing <c>UserId</c> (must match authenticated user).</param>
    /// <returns>
    /// 200 with <c>{ url }</c> — the Stripe-hosted portal URL;
    /// 403 if <c>UserId</c> does not match the JWT subject;
    /// 500 on Stripe API errors.
    /// </returns>
    [HttpPost("create-portal-session")]
    [Authorize]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> CreatePortalSession([FromBody] CreatePortalRequest req)
    {
        try
        {
            EnsureStripeApiKey();

            if (!ValidateUserAccess(req.UserId, out var uid))
                return Forbid();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
            if (user is null) return BadRequest("User not found");

            var customerId = await EnsureValidCustomerIdAsync(user);
            var domain = GetPublicDomain();

            var portal = await new Stripe.BillingPortal.SessionService().CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions
                {
                    Customer = customerId,
                    // ✅ tells frontend it returned from portal so it re-fetches
                    ReturnUrl = $"{domain}/settings?tab=subscription&portal=return"
                }
            );

            return Ok(new { url = portal.Url });
        }
        catch (StripeException ex)
        {
            _log.LogError(ex,
                "Stripe error creating portal session. Code={Code} Msg={Msg}",
                ex.StripeError?.Code, ex.StripeError?.Message);

            return StatusCode(500, $"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error creating portal session");
            return StatusCode(500, "Could not open customer portal.");
        }
    }

    /// <summary>
    /// Returns the authenticated user's current billing and trial status for the Settings UI.
    ///
    /// Performs a best-effort live sync from Stripe before responding: if the user has a
    /// <c>StripeSubscriptionId</c>, the subscription is fetched from Stripe and
    /// <see cref="MirrorSubscriptionToUser"/> is called so that portal-initiated actions
    /// (cancellation, plan changes) are reflected immediately without waiting for a webhook.
    ///
    /// Trial fields are derived from the DB (<c>TrialEndUtc</c>) rather than Stripe — the
    /// 7-day free trial is granted at registration and is independent of Stripe subscriptions.
    /// </summary>
    /// <param name="userId">Must match the authenticated user's JWT subject claim.</param>
    /// <returns>
    /// 200 with billing and trial status object including:
    ///   <c>isPremium</c>, <c>premiumStatus</c>, <c>subscriptionId</c>, <c>currentPeriodEnd</c>,
    ///   <c>cancelAtPeriodEnd</c>, <c>isOnTrial</c>, <c>trialEndsAt</c>, <c>trialDaysRemaining</c>;
    /// 403 if <c>userId</c> does not match the JWT subject;
    /// 404 if user not found.
    /// </returns>
    [HttpGet("status")]
    [Authorize]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> GetStatus([FromQuery] Guid userId)
    {
        var authUserId = GetAuthenticatedUserId();
        if (authUserId == null || authUserId.Value != userId)
            return Forbid();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(user.StripeSubscriptionId))
        {
            try
            {
                EnsureStripeApiKey();

                var subService = new SubscriptionService();
                var sub = await subService.GetAsync(user.StripeSubscriptionId);

                MirrorSubscriptionToUser(user, sub);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to sync subscription from Stripe for user {UserId}", userId);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var isOnTrial = !user.IsPremium
            && user.TrialEndUtc.HasValue
            && user.TrialEndUtc.Value > now;

        int? trialDaysRemaining = null;
        if (isOnTrial && user.TrialEndUtc.HasValue)
        {
            var diff = user.TrialEndUtc.Value - now;
            trialDaysRemaining = (int)Math.Ceiling(diff.TotalDays);
        }

        return Ok(new
        {
            isPremium = user.IsPremium,
            premiumStatus = user.PremiumStatus,
            customerId = user.StripeCustomerId,
            subscriptionId = user.StripeSubscriptionId,
            currentPeriodEnd = user.PremiumCurrentPeriodEnd,
            cancelAtPeriodEnd = user.PremiumCancelAtPeriodEnd,
            isOnTrial,
            trialEndsAt = user.TrialEndUtc,
            trialDaysRemaining
        });
    }

    /// <summary>
    /// Schedules the user's subscription to cancel at the end of the current billing period
    /// by setting <c>cancel_at_period_end = true</c> on the Stripe subscription.
    /// The user retains Premium access until <c>currentPeriodEnd</c>.
    /// </summary>
    /// <param name="req">Request body containing <c>UserId</c> (must match authenticated user).</param>
    /// <returns>
    /// 200 with <c>{ success, message, currentPeriodEnd, cancelAtPeriodEnd }</c>;
    /// 400 if no active subscription; 403 on user mismatch; 500 on Stripe errors.
    /// Side effects: calls <see cref="MirrorSubscriptionToUser"/> and saves to DB.
    /// </returns>
    [HttpPost("cancel-subscription")]
    [Authorize]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> CancelSubscription([FromBody] CancelRequest req)
    {
        try
        {
            EnsureStripeApiKey();

            if (!ValidateUserAccess(req.UserId, out var uid))
                return Forbid();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
            if (user is null) return BadRequest("User not found");

            if (string.IsNullOrWhiteSpace(user.StripeSubscriptionId))
                return BadRequest("No active subscription found");

            var subService = new SubscriptionService();
            var sub = await subService.UpdateAsync(user.StripeSubscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = true
            });

            MirrorSubscriptionToUser(user, sub);
            await _db.SaveChangesAsync();

            _log.LogInformation("Subscription {SubId} for user {UserId} set to cancel at period end", sub.Id, uid);

            return Ok(new
            {
                success = true,
                message = "Subscription will be cancelled at the end of the current billing period",
                currentPeriodEnd = user.PremiumCurrentPeriodEnd,
                cancelAtPeriodEnd = user.PremiumCancelAtPeriodEnd
            });
        }
        catch (StripeException ex)
        {
            _log.LogError(ex,
                "Stripe error cancelling subscription. Code={Code} Msg={Msg}",
                ex.StripeError?.Code, ex.StripeError?.Message);

            return StatusCode(500, $"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error cancelling subscription");
            return StatusCode(500, "Could not cancel subscription.");
        }
    }

    /// <summary>
    /// Undoes a pending cancellation by setting <c>cancel_at_period_end = false</c>.
    /// Only valid for subscriptions in "active" or "trialing" status — a fully "canceled"
    /// subscription cannot be resumed; the user must start a new checkout session.
    /// </summary>
    /// <param name="req">Request body containing <c>UserId</c> (must match authenticated user).</param>
    /// <returns>
    /// 200 with <c>{ success, message, currentPeriodEnd, cancelAtPeriodEnd }</c>;
    /// 400 if subscription is already fully canceled or not found;
    /// 403 on user mismatch; 500 on Stripe errors.
    /// Side effects: calls <see cref="MirrorSubscriptionToUser"/> and saves to DB.
    /// </returns>
    [HttpPost("resume-subscription")]
    [Authorize]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> ResumeSubscription([FromBody] ResumeRequest req)
    {
        try
        {
            EnsureStripeApiKey();

            if (!ValidateUserAccess(req.UserId, out var uid))
                return Forbid();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
            if (user is null) return BadRequest("User not found");

            if (string.IsNullOrWhiteSpace(user.StripeSubscriptionId))
                return BadRequest("No subscription found");

            var subService = new SubscriptionService();

            // First check if the subscription can be resumed
            var currentSub = await subService.GetAsync(user.StripeSubscriptionId);
            if (currentSub.Status == "canceled")
                return BadRequest("Subscription has already been cancelled and cannot be resumed. Please start a new subscription.");

            var sub = await subService.UpdateAsync(user.StripeSubscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = false
            });

            MirrorSubscriptionToUser(user, sub);
            await _db.SaveChangesAsync();

            _log.LogInformation("Subscription {SubId} for user {UserId} resumed (cancel at period end removed)", sub.Id, uid);

            return Ok(new
            {
                success = true,
                message = "Subscription resumed successfully",
                currentPeriodEnd = user.PremiumCurrentPeriodEnd,
                cancelAtPeriodEnd = user.PremiumCancelAtPeriodEnd
            });
        }
        catch (StripeException ex)
        {
            _log.LogError(ex,
                "Stripe error resuming subscription. Code={Code} Msg={Msg}",
                ex.StripeError?.Code, ex.StripeError?.Message);

            return StatusCode(500, $"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error resuming subscription");
            return StatusCode(500, "Could not resume subscription.");
        }
    }
}
