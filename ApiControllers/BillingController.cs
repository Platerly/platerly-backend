using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Contracts.Billing;
using Platerly.Api.Data;
using Stripe;
using Stripe.Checkout;

namespace Platerly.Api.Controllers;

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
        _cfg = cfg; _db = db; _env = env; _log = log;
    }

    // -----------------------------
    // Create Stripe Checkout Session
    // -----------------------------
    [HttpPost("create-checkout-session")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateCheckoutRequest req)
    {
        if (!Guid.TryParse(req.UserId, out var uid)) return BadRequest("Invalid userId");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return BadRequest("User not found");

        // Ensure Stripe Customer exists (prefills/locks email)
        var customerId = user.StripeCustomerId;
        if (string.IsNullOrWhiteSpace(customerId))
        {
            var custSvc = new CustomerService();
            var cust = await custSvc.CreateAsync(new CustomerCreateOptions
            {
                Email = user.Email
               
            });
            customerId = cust.Id;

            var tracked = await _db.Users.FirstAsync(u => u.Id == uid);
            tracked.StripeCustomerId = customerId;
            await _db.SaveChangesAsync();
        }
        else
        {
            try
            {
                var custSvc = new CustomerService();
                await custSvc.UpdateAsync(customerId, new CustomerUpdateOptions { Email = user.Email });
            }
            catch (Exception ex) { _log.LogWarning(ex, "Unable to sync customer email"); }
        }

        var domain = _env.IsDevelopment()
            ? _cfg["Stripe:PublicDomainDev"] ?? "http://localhost:5173"
            : _cfg["Stripe:PublicDomainProd"] ?? "https://platerly.com";

        // 👉 land users back on the Settings page, Subscription tab
        var successUrl = $"{domain}/settings?tab=subscription&sub=success";
        var cancelUrl = $"{domain}/settings?tab=subscription&sub=cancel";

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,

            Customer = customerId,                          // prefill + no email box
            PaymentMethodTypes = new List<string> { "card" }, // card only (no Klarna/Link)

            LineItems = new()
            {
                new() { Price = _cfg["Stripe:PriceIdPremium"], Quantity = 1 }
            },

            AllowPromotionCodes = bool.TryParse(_cfg["Stripe:AllowPromoCodes"], out var apc) && apc,

            ClientReferenceId = req.UserId,
            SubscriptionData = new() { Metadata = new() { ["pl_user_id"] = req.UserId } },
            Metadata = new() { ["pl_user_id"] = req.UserId },

            BillingAddressCollection = "auto",
            Locale = "auto"
        };

        if (bool.TryParse(_cfg["Stripe:EnableAutomaticTax"], out var tax) && tax)
            options.AutomaticTax = new() { Enabled = true };

        var session = await new SessionService().CreateAsync(
            options,
            new RequestOptions { IdempotencyKey = $"chk:{user.Id}:{DateTime.UtcNow:yyyyMMddHHmm}" }
        );

        _log.LogInformation("Created checkout session for {UserId} -> {Url}", user.Id, session.Url);
        return Ok(new { url = session.Url });
    }

    // -----------------------------
    // Customer Portal
    // -----------------------------
    [HttpPost("create-portal-session")]
    public async Task<IActionResult> CreatePortalSession([FromBody] CreatePortalRequest req)
    {
        if (!Guid.TryParse(req.UserId, out var uid)) return BadRequest("Invalid userId");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null || string.IsNullOrWhiteSpace(user.StripeCustomerId))
            return BadRequest("User not found or no Stripe customer");

        var domain = _env.IsDevelopment()
            ? _cfg["Stripe:PublicDomainDev"] ?? "http://localhost:5173"
            : _cfg["Stripe:PublicDomainProd"] ?? "https://platerly.com";

        var portal = await new Stripe.BillingPortal.SessionService().CreateAsync(new()
        {
            Customer = user.StripeCustomerId,
            ReturnUrl = $"{domain}/settings?tab=subscription"
        });

        return Ok(new { url = portal.Url });
    }

    // -----------------------------
    // Billing Status (for Settings UI)
    // -----------------------------
    // GET /api/billing/status?userId=GUID
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
