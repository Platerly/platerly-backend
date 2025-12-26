using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Contracts.Billing;
using Platerly.Api.Data;
using Stripe;
using Stripe.Checkout;

namespace Platerly.Api.Controllers; 

using UserEntity = Platerly.Api.Models.User;

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
    // Create Stripe Checkout Session
    // -----------------------------
    [HttpPost("create-checkout-session")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateCheckoutRequest req)
    {
        try
        {
            EnsureStripeApiKey();

            if (!Guid.TryParse(req.UserId, out var uid))
                return BadRequest("Invalid userId");

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
    // Customer Portal
    // -----------------------------
    [HttpPost("create-portal-session")]
    public async Task<IActionResult> CreatePortalSession([FromBody] CreatePortalRequest req)
    {
        try
        {
            EnsureStripeApiKey();

            if (!Guid.TryParse(req.UserId, out var uid))
                return BadRequest("Invalid userId");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
            if (user is null) return BadRequest("User not found");

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
    public async Task<IActionResult> GetStatus([FromQuery] Guid userId)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
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
}
