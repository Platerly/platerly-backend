using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Contracts.Billing;
using Platerly.Api.Data;
using Stripe;
using Stripe.Checkout;

namespace Platerly.Api.Controllers;

using UserEntity = Platerly.Api.Models.User;

[Authorize]
[ApiController]
[Route("api/billing")]
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

    /// <summary>
    /// Cleanly derive userId from JWT "sub". Optional fallback to body userId for backwards compatibility.
    /// </summary>
    private Guid? GetCallerUserIdOrNull(string? fallbackUserId)
    {
        var claim =
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
            User.FindFirst("sub")?.Value ??
            User.FindFirst("nameid")?.Value ??
            User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (Guid.TryParse(claim, out var fromJwt))
            return fromJwt;

        if (!string.IsNullOrWhiteSpace(fallbackUserId) && Guid.TryParse(fallbackUserId, out var fromBody))
            return fromBody;

        return null;
    }

    private async Task<UserEntity?> LoadCallerUserOrNullAsync(string? fallbackUserId)
    {
        var uid = GetCallerUserIdOrNull(fallbackUserId);
        if (uid is null) return null;

        return await _db.Users.FirstOrDefaultAsync(u => u.Id == uid.Value);
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
                // fall through -> create new
            }
        }

        var created = await custSvc.CreateAsync(new CustomerCreateOptions
        {
            Email = user.Email,
            Metadata = new Dictionary<string, string> { ["pl_user_id"] = user.Id.ToString() }
        });

        user.StripeCustomerId = created.Id;
        await _db.SaveChangesAsync();

        return created.Id;
    }

    private static DateTimeOffset? GetCurrentPeriodEndFromSubscription(Subscription sub)
    {
        var periodEnd = sub.Items?.Data?
            .Select(i => i.CurrentPeriodEnd)
            .Where(d => d != null)
            .OrderBy(d => d)
            .FirstOrDefault();

        return periodEnd is null ? null : new DateTimeOffset(periodEnd.Value);
    }

    private void MirrorSubscriptionToUser(UserEntity user, Subscription sub)
    {
        user.StripeSubscriptionId = sub.Id;
        user.PremiumStatus = sub.Status;
        user.IsPremium = sub.Status is "active" or "trialing";
        user.PremiumCancelAtPeriodEnd = sub.CancelAtPeriodEnd;
        user.PremiumCurrentPeriodEnd = GetCurrentPeriodEndFromSubscription(sub);
        user.PremiumSince ??= DateTimeOffset.UtcNow;
    }

    // -----------------------------
    // Create Stripe Checkout Session
    // -----------------------------
    // IMPORTANT: req is nullable so frontend can send {} (or even no body) without 400.
    [HttpPost("create-checkout-session")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateCheckoutRequest? req)
    {
        try
        {
            EnsureStripeApiKey();

            var user = await LoadCallerUserOrNullAsync(req?.UserId);
            if (user is null) return Unauthorized("User not found or not authenticated.");

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

                ClientReferenceId = user.Id.ToString(),
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string> { ["pl_user_id"] = user.Id.ToString() }
                },
                Metadata = new Dictionary<string, string> { ["pl_user_id"] = user.Id.ToString() },

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
                new RequestOptions { IdempotencyKey = $"chk:{user.Id}:{DateTime.UtcNow:yyyyMMddHHmm}" }
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
    // Customer Portal
    // -----------------------------
    [HttpPost("create-portal-session")]
    public async Task<IActionResult> CreatePortalSession([FromBody] CreatePortalRequest? req)
    {
        try
        {
            EnsureStripeApiKey();

            var user = await LoadCallerUserOrNullAsync(req?.UserId);
            if (user is null) return Unauthorized("User not found or not authenticated.");

            var customerId = await EnsureValidCustomerIdAsync(user);
            var domain = GetPublicDomain();

            var portal = await new Stripe.BillingPortal.SessionService().CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions
                {
                    Customer = customerId,
                    ReturnUrl = $"{domain}/settings?tab=subscription"
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
    // -----------------------------
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus([FromQuery] Guid? userId = null)
    {
        // Prefer JWT
        Guid? jwtUid = GetCallerUserIdOrNull(null);
        var uid = jwtUid ?? userId;

        if (uid is null) return Unauthorized();

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid.Value);
        if (user is null) return NotFound();

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
    // Cancel at Period End
    // -----------------------------
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel([FromBody] CancelRequest? req)
    {
        EnsureStripeApiKey();

        var user = await LoadCallerUserOrNullAsync(req?.UserId);
        if (user is null) return Unauthorized("User not found or not authenticated.");

        var subId = user.StripeSubscriptionId;
        if (string.IsNullOrWhiteSpace(subId))
            return BadRequest("No active subscription");

        var subService = new SubscriptionService();
        var current = await subService.GetAsync(subId);

        if (!(current.Status is "active" or "trialing"))
            return Conflict($"Cannot cancel subscription in status '{current.Status}'.");

        var updated = await subService.UpdateAsync(subId, new SubscriptionUpdateOptions
        {
            CancelAtPeriodEnd = true
        });

        MirrorSubscriptionToUser(user, updated);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            isPremium = user.IsPremium,
            premiumStatus = user.PremiumStatus,
            cancelAtPeriodEnd = user.PremiumCancelAtPeriodEnd,
            currentPeriodEnd = user.PremiumCurrentPeriodEnd
        });
    }

    // -----------------------------
    // Resume (undo cancel_at_period_end)
    // -----------------------------
    [HttpPost("resume")]
    public async Task<IActionResult> Resume([FromBody] ResumeRequest? req)
    {
        EnsureStripeApiKey();

        var user = await LoadCallerUserOrNullAsync(req?.UserId);
        if (user is null) return Unauthorized("User not found or not authenticated.");

        var subId = user.StripeSubscriptionId;
        if (string.IsNullOrWhiteSpace(subId))
            return BadRequest("No active subscription");

        var subService = new SubscriptionService();
        var current = await subService.GetAsync(subId);

        if (!current.CancelAtPeriodEnd || !(current.Status is "active" or "trialing"))
            return Conflict("Subscription cannot be resumed; it is not in a resumable state.");

        var updated = await subService.UpdateAsync(subId, new SubscriptionUpdateOptions
        {
            CancelAtPeriodEnd = false
        });

        MirrorSubscriptionToUser(user, updated);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            isPremium = user.IsPremium,
            premiumStatus = user.PremiumStatus,
            cancelAtPeriodEnd = user.PremiumCancelAtPeriodEnd,
            currentPeriodEnd = user.PremiumCurrentPeriodEnd
        });
    }
}
