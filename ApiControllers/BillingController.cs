using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Contracts.Billing;
using Platerly.Api.Data;
using Stripe;
using Stripe.Checkout;

namespace Platerly.Api.Controllers;

using UserEntity = Platerly.Api.Models.User;

[ApiController]
[Route("api/billing")]
[Authorize]
public class BillingController : ControllerBase
{
    private readonly IConfiguration _cfg;
    private readonly PlaterlyDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<BillingController> _log;

    public BillingController(IConfiguration cfg, PlaterlyDbContext db, IWebHostEnvironment env, ILogger<BillingController> log)
    {
        _cfg = cfg;
        _db = db;
        _env = env;
        _log = log;
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
            : (_cfg["Stripe:PublicDomainProd"] ?? "https://platerly.com");
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

    private static string? TryGetStringProperty(object obj, string propName)
    {
        var p = obj.GetType().GetProperty(propName);
        if (p == null) return null;
        return p.GetValue(obj) as string;
    }

    private static object? TryGetProperty(object obj, string propName)
    {
        var p = obj.GetType().GetProperty(propName);
        return p?.GetValue(obj);
    }

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

    // -----------------------------
    // Create Stripe Checkout Session
    // -----------------------------
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

    // -----------------------------
    // Stripe Webhook (updates DB)
    // -----------------------------
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
    }

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

    // -----------------------------
    // Customer Portal
    // -----------------------------
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

    // -----------------------------
    // Billing Status (for Settings UI)
    // ✅ BEST-EFFORT live sync from Stripe so portal actions reflect immediately
    // -----------------------------
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

        return Ok(new
        {
            isPremium = user.IsPremium,
            premiumStatus = user.PremiumStatus,
            customerId = user.StripeCustomerId,
            subscriptionId = user.StripeSubscriptionId,
            currentPeriodEnd = user.PremiumCurrentPeriodEnd,
            cancelAtPeriodEnd = user.PremiumCancelAtPeriodEnd
        });
    }

    // -----------------------------
    // Cancel Subscription (at period end)
    // -----------------------------
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

    // -----------------------------
    // Resume Subscription (undo cancel at period end)
    // -----------------------------
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
