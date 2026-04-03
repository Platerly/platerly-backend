using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Contracts.B2BBilling;
using Platerly.Api.Data;
using Platerly.Api.Models;
using Stripe;
using Stripe.Checkout;

namespace Platerly.Api.Controllers;

/// <summary>
/// B2B Billing controller. Manages Stripe subscriptions for business (B2B) accounts.
///
/// Base route: /api/b2b-billing
/// Auth: [Authorize] (JWT) on all endpoints except the webhook.
///
/// This controller is separate from <c>BillingController</c> because:
///   • B2B accounts map to <see cref="B2BAccount"/> (not directly to <see cref="User"/> fields)
///   • B2B has multiple price tiers (starter / professional / enterprise)
///   • The B2B Stripe webhook uses a different secret (<c>StripeB2B:WebhookSecret</c>)
///
/// Plan tier configuration (appsettings / environment variables):
///   StripeB2B:PriceIdStarterMonthly
///   StripeB2B:PriceIdStarterAnnual
///   StripeB2B:PriceIdProfessionalMonthly
///   StripeB2B:PriceIdProfessionalAnnual
///   StripeB2B:PriceIdEnterpriseMonthly
///   StripeB2B:PriceIdEnterpriseAnnual
///
/// Tier slugs are inferred from the price ID at webhook time via
/// <see cref="TierFromPriceId"/>. Update that mapping when real price IDs are plugged in.
/// </summary>
[ApiController]
[Route("api/b2b-billing")]
[Authorize]
public class B2BBillingController : ControllerBase
{
    private readonly IConfiguration _cfg;
    private readonly PlaterlyDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<B2BBillingController> _log;

    public B2BBillingController(
        IConfiguration cfg,
        PlaterlyDbContext db,
        IWebHostEnvironment env,
        ILogger<B2BBillingController> log)
    {
        _cfg = cfg;
        _db = db;
        _env = env;
        _log = log;
    }

    // ── Auth helpers ──────────────────────────────────────────────────────────

    private Guid? GetAuthUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("nameid")?.Value;
        return Guid.TryParse(claim, out var uid) ? uid : null;
    }

    private bool ValidateUserAccess(string requestedUserId, out Guid userId)
    {
        var authId = GetAuthUserId();
        if (authId == null || !Guid.TryParse(requestedUserId, out userId))
        {
            userId = Guid.Empty;
            return false;
        }
        return userId == authId.Value;
    }

    // ── Stripe helpers ────────────────────────────────────────────────────────

    private void EnsureStripeApiKey()
    {
        var key = _cfg["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Stripe:SecretKey is not configured.");
        StripeConfiguration.ApiKey = key;
    }

    private string GetPublicDomain() =>
        _env.IsDevelopment()
            ? (_cfg["Stripe:PublicDomainDev"] ?? "http://localhost:5173")
            : (_cfg["Stripe:PublicDomainProd"] ?? "https://recipzee.com");

    /// <summary>
    /// Returns the set of all valid B2B price IDs from configuration.
    /// Used to validate that a requested price ID is one we recognise.
    /// </summary>
    private HashSet<string> GetValidB2BPriceIds()
    {
        var keys = new[]
        {
            "StripeB2B:PriceIdStarterMonthly",
            "StripeB2B:PriceIdStarterAnnual",
            "StripeB2B:PriceIdProfessionalMonthly",
            "StripeB2B:PriceIdProfessionalAnnual",
            "StripeB2B:PriceIdEnterpriseMonthly",
            "StripeB2B:PriceIdEnterpriseAnnual",
        };

        return keys
            .Select(k => _cfg[k])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet()!;
    }

    /// <summary>
    /// Maps a Stripe price ID back to a tier slug.
    /// Called during webhook handling to set <see cref="B2BAccount.PlanTier"/>.
    /// </summary>
    private string? TierFromPriceId(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId)) return null;

        if (priceId == _cfg["StripeB2B:PriceIdStarterMonthly"] ||
            priceId == _cfg["StripeB2B:PriceIdStarterAnnual"])
            return "starter";

        if (priceId == _cfg["StripeB2B:PriceIdProfessionalMonthly"] ||
            priceId == _cfg["StripeB2B:PriceIdProfessionalAnnual"])
            return "professional";

        if (priceId == _cfg["StripeB2B:PriceIdEnterpriseMonthly"] ||
            priceId == _cfg["StripeB2B:PriceIdEnterpriseAnnual"])
            return "enterprise";

        return null;
    }

    // ── Stripe.NET version-safe helpers (same pattern as BillingController) ───

    private static string? TryGetStringProperty(object obj, string propName)
    {
        var p = obj.GetType().GetProperty(propName);
        return p?.GetValue(obj) as string;
    }

    private static object? TryGetProperty(object obj, string propName) =>
        obj.GetType().GetProperty(propName)?.GetValue(obj);

    private static string? ExtractStripeId(object? value)
    {
        if (value == null) return null;
        if (value is string s) return string.IsNullOrWhiteSpace(s) ? null : s;
        if (value is Customer c) return c.Id;
        if (value is Subscription sub) return sub.Id;

        var idProp = value.GetType().GetProperty("Id");
        if (idProp?.PropertyType == typeof(string))
        {
            var idVal = idProp.GetValue(value) as string;
            if (!string.IsNullOrWhiteSpace(idVal)) return idVal;
        }

        var valueProp = value.GetType().GetProperty("Value");
        var inner = valueProp?.GetValue(value);
        if (inner != null)
        {
            var innerId = inner.GetType().GetProperty("Id")?.GetValue(inner) as string;
            if (!string.IsNullOrWhiteSpace(innerId)) return innerId;
        }

        return null;
    }

    private static string? GetSessionCustomerIdSafe(Session session)
    {
        var id = TryGetStringProperty(session, "CustomerId");
        if (!string.IsNullOrWhiteSpace(id)) return id;
        return ExtractStripeId(TryGetProperty(session, "Customer"));
    }

    private static string? GetSessionSubscriptionIdSafe(Session session)
    {
        var id = TryGetStringProperty(session, "SubscriptionId");
        if (!string.IsNullOrWhiteSpace(id)) return id;
        return ExtractStripeId(TryGetProperty(session, "Subscription"));
    }

    private static string? GetSubscriptionCustomerIdSafe(Subscription sub)
    {
        var id = TryGetStringProperty(sub, "CustomerId");
        if (!string.IsNullOrWhiteSpace(id)) return id;
        return ExtractStripeId(TryGetProperty(sub, "Customer"));
    }

    private static DateTimeOffset? GetCurrentPeriodEndSafe(Subscription sub)
    {
        var prop = sub.GetType().GetProperty("CurrentPeriodEnd");
        if (prop != null)
        {
            var val = prop.GetValue(sub);
            if (val is DateTime dt) return new DateTimeOffset(dt);
            if (val is DateTimeOffset dto) return dto;
            if (val is long l) return DateTimeOffset.FromUnixTimeSeconds(l);
            if (val is int i) return DateTimeOffset.FromUnixTimeSeconds(i);
        }

        var itemEnd = sub.Items?.Data?
            .Select(i => i.CurrentPeriodEnd)
            .Where(d => d != null)
            .OrderBy(d => d)
            .FirstOrDefault();

        return itemEnd is null ? null : new DateTimeOffset(itemEnd.Value);
    }

    private static DateTimeOffset? GetCancelAtSafe(Subscription sub)
    {
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
    /// Extracts the first price ID from a subscription's line items.
    /// Used to infer the plan tier on subscription events.
    /// </summary>
    private static string? GetFirstPriceId(Subscription sub) =>
        sub.Items?.Data?.FirstOrDefault()?.Price?.Id;

    /// <summary>
    /// Applies the latest Stripe subscription state to a <see cref="B2BAccount"/>.
    /// Mirrors the same pattern as <c>BillingController.MirrorSubscriptionToUser()</c>.
    /// Caller must call <c>_db.SaveChangesAsync()</c> afterwards.
    /// </summary>
    private void MirrorSubscriptionToAccount(B2BAccount account, Subscription sub)
    {
        var cancelAt = GetCancelAtSafe(sub);
        var priceId = GetFirstPriceId(sub);

        account.StripeSubscriptionId = sub.Id;
        account.PlanStatus = sub.Status;
        account.IsActive = sub.Status is "active" or "trialing";
        account.CancelAtPeriodEnd = sub.CancelAtPeriodEnd || cancelAt != null;
        account.CurrentPeriodEnd = GetCurrentPeriodEndSafe(sub);
        account.PlanTier ??= TierFromPriceId(priceId);  // only infer if not already set
        account.UpdatedAt = DateTimeOffset.UtcNow;

        if (account.IsActive)
            account.SubscribedSince ??= DateTimeOffset.UtcNow;
        else
            account.SubscribedSince = null;
    }

    /// <summary>
    /// Gets or creates a Stripe Customer for the given B2B account.
    /// Uses the billing email if set, otherwise falls back to the owner's login email.
    /// </summary>
    private async Task<string> EnsureB2BCustomerIdAsync(B2BAccount account, User owner)
    {
        var custSvc = new CustomerService();
        var email = account.BillingEmail ?? owner.Email;

        if (!string.IsNullOrWhiteSpace(account.StripeCustomerId))
        {
            try
            {
                var existing = await custSvc.GetAsync(account.StripeCustomerId);
                if (!string.Equals(existing.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    try { await custSvc.UpdateAsync(existing.Id, new CustomerUpdateOptions { Email = email }); }
                    catch (Exception ex) { _log.LogWarning(ex, "Could not sync B2B customer email"); }
                }
                return existing.Id;
            }
            catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
            {
                _log.LogWarning("B2B Stripe customer {Id} not found; creating new.", account.StripeCustomerId);
            }
        }

        var created = await custSvc.CreateAsync(new CustomerCreateOptions
        {
            Email = email,
            Name = account.CompanyName,
            Metadata = new Dictionary<string, string>
            {
                ["pl_b2b_account_id"] = account.Id.ToString(),
                ["pl_user_id"] = owner.Id.ToString()
            }
        });

        account.StripeCustomerId = created.Id;
        await _db.SaveChangesAsync();
        return created.Id;
    }

    // ── Endpoints ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current B2B billing status for the authenticated user.
    ///
    /// Performs a live sync from Stripe before responding (same pattern as B2C GetStatus).
    /// If no B2B account exists yet, returns a zero-state response with IsActive=false.
    /// </summary>
    [HttpGet("status")]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> GetStatus([FromQuery] Guid userId)
    {
        var authId = GetAuthUserId();
        if (authId == null || authId.Value != userId) return Forbid();

        var account = await _db.B2BAccounts.FirstOrDefaultAsync(a => a.UserId == userId);

        if (account != null && !string.IsNullOrWhiteSpace(account.StripeSubscriptionId))
        {
            try
            {
                EnsureStripeApiKey();
                var sub = await new SubscriptionService().GetAsync(account.StripeSubscriptionId);
                MirrorSubscriptionToAccount(account, sub);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "B2B live sync failed for user {UserId}", userId);
            }
        }

        return Ok(new B2BStatusResponse(
            IsActive: account?.IsActive ?? false,
            PlanTier: account?.PlanTier,
            PlanStatus: account?.PlanStatus,
            StripeCustomerId: account?.StripeCustomerId,
            StripeSubscriptionId: account?.StripeSubscriptionId,
            CurrentPeriodEnd: account?.CurrentPeriodEnd,
            CancelAtPeriodEnd: account?.CancelAtPeriodEnd ?? false,
            SubscribedSince: account?.SubscribedSince,
            CompanyName: account?.CompanyName,
            BillingEmail: account?.BillingEmail
        ));
    }

    /// <summary>
    /// Creates a Stripe Checkout Session for a B2B plan.
    ///
    /// The requested <c>PriceId</c> must match one of the configured
    /// <c>StripeB2B:PriceId*</c> values; unknown price IDs are rejected with 400.
    /// Embeds <c>pl_b2b_account_id</c> in metadata so the webhook can map events back.
    /// </summary>
    [HttpPost("create-checkout-session")]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateB2BCheckoutRequest req)
    {
        try
        {
            EnsureStripeApiKey();

            if (!ValidateUserAccess(req.UserId, out var uid)) return Forbid();

            var owner = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
            if (owner is null) return BadRequest("User not found.");

            var validPriceIds = GetValidB2BPriceIds();
            if (!validPriceIds.Contains(req.PriceId))
                return BadRequest("Invalid or unconfigured B2B price ID.");

            // Get or create the B2BAccount record
            var account = await _db.B2BAccounts.FirstOrDefaultAsync(a => a.UserId == uid);
            if (account is null)
            {
                account = new B2BAccount { UserId = uid };
                _db.B2BAccounts.Add(account);
                await _db.SaveChangesAsync();
            }

            var customerId = await EnsureB2BCustomerIdAsync(account, owner);
            var domain = GetPublicDomain();

            var options = new SessionCreateOptions
            {
                Mode = "subscription",
                SuccessUrl = $"{domain}/b2b/billing?sub=success",
                CancelUrl = $"{domain}/b2b/billing?sub=cancel",
                Customer = customerId,
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new() { Price = req.PriceId, Quantity = 1 }
                },
                AllowPromotionCodes = bool.TryParse(_cfg["Stripe:AllowPromoCodes"], out var apc) && apc,
                ClientReferenceId = account.Id.ToString(),
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string>
                    {
                        ["pl_b2b_account_id"] = account.Id.ToString(),
                        ["pl_user_id"] = uid.ToString()
                    }
                },
                Metadata = new Dictionary<string, string>
                {
                    ["pl_b2b_account_id"] = account.Id.ToString(),
                    ["pl_user_id"] = uid.ToString()
                },
                Locale = "auto"
            };

            var session = await new SessionService().CreateAsync(
                options,
                new RequestOptions
                {
                    IdempotencyKey = $"b2b_chk:{account.Id}:{DateTime.UtcNow:yyyyMMddHHmm}"
                }
            );

            _log.LogInformation("Created B2B checkout session for account {AccountId} -> {Url}", account.Id, session.Url);
            return Ok(new { url = session.Url });
        }
        catch (StripeException ex)
        {
            _log.LogError(ex, "Stripe error creating B2B checkout session. Code={Code}", ex.StripeError?.Code);
            return StatusCode(500, $"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error creating B2B checkout session");
            return StatusCode(500, "Could not start checkout.");
        }
    }

    /// <summary>
    /// Creates a Stripe Customer Portal Session for self-serve B2B plan management.
    /// Return URL appends <c>?portal=return</c> so the frontend re-fetches status.
    /// </summary>
    [HttpPost("create-portal-session")]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> CreatePortalSession([FromBody] CreateB2BPortalRequest req)
    {
        try
        {
            EnsureStripeApiKey();

            if (!ValidateUserAccess(req.UserId, out var uid)) return Forbid();

            var owner = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
            if (owner is null) return BadRequest("User not found.");

            var account = await _db.B2BAccounts.FirstOrDefaultAsync(a => a.UserId == uid);
            if (account is null || string.IsNullOrWhiteSpace(account.StripeCustomerId))
                return BadRequest("No B2B billing account found. Please subscribe first.");

            var domain = GetPublicDomain();

            var portal = await new Stripe.BillingPortal.SessionService().CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions
                {
                    Customer = account.StripeCustomerId,
                    ReturnUrl = $"{domain}/b2b/billing?portal=return"
                }
            );

            return Ok(new { url = portal.Url });
        }
        catch (StripeException ex)
        {
            _log.LogError(ex, "Stripe error creating B2B portal session. Code={Code}", ex.StripeError?.Code);
            return StatusCode(500, $"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error creating B2B portal session");
            return StatusCode(500, "Could not open portal.");
        }
    }

    /// <summary>
    /// Schedules the B2B subscription to cancel at the end of the current billing period.
    /// </summary>
    [HttpPost("cancel-subscription")]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> CancelSubscription([FromBody] CancelB2BSubscriptionRequest req)
    {
        try
        {
            EnsureStripeApiKey();

            if (!ValidateUserAccess(req.UserId, out var uid)) return Forbid();

            var account = await _db.B2BAccounts.FirstOrDefaultAsync(a => a.UserId == uid);
            if (account is null || string.IsNullOrWhiteSpace(account.StripeSubscriptionId))
                return BadRequest("No active B2B subscription found.");

            var sub = await new SubscriptionService().UpdateAsync(
                account.StripeSubscriptionId,
                new SubscriptionUpdateOptions { CancelAtPeriodEnd = true }
            );

            MirrorSubscriptionToAccount(account, sub);
            await _db.SaveChangesAsync();

            _log.LogInformation("B2B subscription {SubId} set to cancel at period end for account {AccountId}", sub.Id, account.Id);

            return Ok(new
            {
                success = true,
                message = "Subscription will be cancelled at the end of the current billing period.",
                currentPeriodEnd = account.CurrentPeriodEnd,
                cancelAtPeriodEnd = account.CancelAtPeriodEnd
            });
        }
        catch (StripeException ex)
        {
            _log.LogError(ex, "Stripe error cancelling B2B subscription. Code={Code}", ex.StripeError?.Code);
            return StatusCode(500, $"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error cancelling B2B subscription");
            return StatusCode(500, "Could not cancel subscription.");
        }
    }

    /// <summary>
    /// Removes a pending B2B subscription cancellation (sets cancel_at_period_end = false).
    /// </summary>
    [HttpPost("resume-subscription")]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> ResumeSubscription([FromBody] ResumeB2BSubscriptionRequest req)
    {
        try
        {
            EnsureStripeApiKey();

            if (!ValidateUserAccess(req.UserId, out var uid)) return Forbid();

            var account = await _db.B2BAccounts.FirstOrDefaultAsync(a => a.UserId == uid);
            if (account is null || string.IsNullOrWhiteSpace(account.StripeSubscriptionId))
                return BadRequest("No B2B subscription found.");

            var subSvc = new SubscriptionService();
            var current = await subSvc.GetAsync(account.StripeSubscriptionId);
            if (current.Status == "canceled")
                return BadRequest("Subscription has already been cancelled. Please start a new subscription.");

            var sub = await subSvc.UpdateAsync(
                account.StripeSubscriptionId,
                new SubscriptionUpdateOptions { CancelAtPeriodEnd = false }
            );

            MirrorSubscriptionToAccount(account, sub);
            await _db.SaveChangesAsync();

            _log.LogInformation("B2B subscription {SubId} resumed for account {AccountId}", sub.Id, account.Id);

            return Ok(new
            {
                success = true,
                message = "Subscription resumed successfully.",
                currentPeriodEnd = account.CurrentPeriodEnd,
                cancelAtPeriodEnd = account.CancelAtPeriodEnd
            });
        }
        catch (StripeException ex)
        {
            _log.LogError(ex, "Stripe error resuming B2B subscription. Code={Code}", ex.StripeError?.Code);
            return StatusCode(500, $"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error resuming B2B subscription");
            return StatusCode(500, "Could not resume subscription.");
        }
    }

    /// <summary>
    /// Updates the company name and/or billing email on the B2B account.
    /// Creates the account record if it doesn't exist yet.
    /// </summary>
    [HttpPatch("account")]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> UpdateAccount([FromBody] UpdateB2BAccountRequest req)
    {
        if (!ValidateUserAccess(req.UserId, out var uid)) return Forbid();

        var account = await _db.B2BAccounts.FirstOrDefaultAsync(a => a.UserId == uid);
        if (account is null)
        {
            account = new B2BAccount { UserId = uid };
            _db.B2BAccounts.Add(account);
        }

        if (req.CompanyName is not null) account.CompanyName = req.CompanyName;
        if (req.BillingEmail is not null) account.BillingEmail = req.BillingEmail;
        account.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    /// <summary>
    /// Returns the last 24 invoices for the B2B Stripe customer.
    /// Each invoice includes amount, currency, status, and a link to the PDF/hosted invoice.
    /// </summary>
    [HttpGet("invoices")]
    [EnableRateLimiting("general")]
    public async Task<IActionResult> GetInvoices([FromQuery] Guid userId)
    {
        var authId = GetAuthUserId();
        if (authId == null || authId.Value != userId) return Forbid();

        var account = await _db.B2BAccounts.FirstOrDefaultAsync(a => a.UserId == userId);
        if (account is null || string.IsNullOrWhiteSpace(account.StripeCustomerId))
            return Ok(new { invoices = Array.Empty<B2BInvoiceResponse>() });

        try
        {
            EnsureStripeApiKey();

            var invoices = await new InvoiceService().ListAsync(new InvoiceListOptions
            {
                Customer = account.StripeCustomerId,
                Limit = 24
            });

            var result = invoices.Data.Select(inv => new B2BInvoiceResponse(
                InvoiceId: inv.Id,
                Number: inv.Number,
                AmountPaid: inv.AmountPaid,
                Currency: inv.Currency,
                Status: inv.Status ?? "unknown",
                Created: DateTimeOffset.FromUnixTimeSeconds(
                    (long)(inv.GetType().GetProperty("Created")?.GetValue(inv) is long l ? l
                        : inv.GetType().GetProperty("Created")?.GetValue(inv) is DateTime dt ? ((DateTimeOffset)dt).ToUnixTimeSeconds()
                        : 0)),
                PdfUrl: inv.InvoicePdf,
                HostedUrl: inv.HostedInvoiceUrl
            )).ToList();

            return Ok(new { invoices = result });
        }
        catch (StripeException ex)
        {
            _log.LogError(ex, "Stripe error fetching B2B invoices. Code={Code}", ex.StripeError?.Code);
            return StatusCode(500, $"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected error fetching B2B invoices");
            return StatusCode(500, "Could not fetch invoices.");
        }
    }

    /// <summary>
    /// Handles Stripe webhook events for B2B subscriptions.
    ///
    /// Uses a separate webhook secret (<c>StripeB2B:WebhookSecret</c>) so that
    /// B2B and B2C webhooks can be routed to different Stripe webhook endpoints.
    ///
    /// Handled events:
    ///   checkout.session.completed    → activates B2BAccount
    ///   customer.subscription.created → mirrors subscription state
    ///   customer.subscription.updated → updates status, cancel flags, period end
    ///   customer.subscription.deleted → deactivates B2BAccount
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var sigHeader = Request.Headers["Stripe-Signature"].ToString();

        var webhookSecret = _cfg["StripeB2B:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
            return StatusCode(500, "StripeB2B:WebhookSecret is not configured.");

        EnsureStripeApiKey();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json, sigHeader, webhookSecret,
                tolerance: 300,
                throwOnApiVersionMismatch: false
            );
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "B2B webhook construct failed.");
            return BadRequest();
        }

        try
        {
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    await HandleB2BCheckoutCompleted(stripeEvent);
                    break;

                case "customer.subscription.created":
                case "customer.subscription.updated":
                case "customer.subscription.deleted":
                    await HandleB2BSubscriptionChanged(stripeEvent);
                    break;

                default:
                    break;
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "B2B webhook handler error for {EventType}", stripeEvent.Type);
            return StatusCode(500);
        }
    }

    private async Task HandleB2BCheckoutCompleted(Event stripeEvent)
    {
        var session = stripeEvent.Data.Object as Session;
        if (session is null) return;

        // Resolve B2B account via client_reference_id (preferred) or metadata
        var accountIdStr = session.ClientReferenceId;
        if (string.IsNullOrWhiteSpace(accountIdStr) &&
            session.Metadata?.TryGetValue("pl_b2b_account_id", out var mid) == true)
        {
            accountIdStr = mid;
        }

        if (!Guid.TryParse(accountIdStr, out var accountId))
        {
            _log.LogWarning("B2B checkout.session.completed missing pl_b2b_account_id.");
            return;
        }

        var account = await _db.B2BAccounts.FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null)
        {
            _log.LogWarning("No B2BAccount found for id={AccountId}", accountId);
            return;
        }

        var customerId = GetSessionCustomerIdSafe(session);
        var subId = GetSessionSubscriptionIdSafe(session);

        if (!string.IsNullOrWhiteSpace(customerId))
            account.StripeCustomerId = customerId;

        if (!string.IsNullOrWhiteSpace(subId))
        {
            var sub = await new SubscriptionService().GetAsync(subId);
            account.PlanTier = TierFromPriceId(GetFirstPriceId(sub));
            MirrorSubscriptionToAccount(account, sub);
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("B2B checkout completed for account {AccountId}", accountId);
    }

    private async Task HandleB2BSubscriptionChanged(Event stripeEvent)
    {
        var sub = stripeEvent.Data.Object as Subscription;
        if (sub is null) return;

        var customerId = GetSubscriptionCustomerIdSafe(sub);
        if (string.IsNullOrWhiteSpace(customerId)) return;

        var account = await _db.B2BAccounts.FirstOrDefaultAsync(a => a.StripeCustomerId == customerId);
        if (account is null)
        {
            _log.LogWarning("No B2BAccount found for StripeCustomerId={CustomerId}", customerId);
            return;
        }

        // Update tier if we can resolve it (handles plan changes via portal)
        var newTier = TierFromPriceId(GetFirstPriceId(sub));
        if (newTier is not null) account.PlanTier = newTier;

        MirrorSubscriptionToAccount(account, sub);
        await _db.SaveChangesAsync();
    }
}
