using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;
using Stripe;

namespace Platerly.Api.Controllers;

[ApiController]
[Route("stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly IConfiguration _cfg;
    private readonly PlaterlyDbContext _db;
    private readonly ILogger<StripeWebhookController> _log;

    public StripeWebhookController(IConfiguration cfg, PlaterlyDbContext db, ILogger<StripeWebhookController> log)
    {
        _cfg = cfg; _db = db; _log = log;
    }

    [HttpPost("webhook")]
    [IgnoreAntiforgeryToken] // if you use antiforgery elsewhere
    public async Task<IActionResult> Handle()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var sig = Request.Headers["Stripe-Signature"].ToString();
        var wh = _cfg["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(sig) || string.IsNullOrWhiteSpace(wh))
            return BadRequest();

        Event evt;
        try
        {
            // keep version-tolerant locally
            evt = EventUtility.ConstructEvent(json, sig, wh, throwOnApiVersionMismatch: false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Stripe signature invalid");
            return BadRequest();
        }

        try
        {
            switch (evt.Type)
            {
                case "checkout.session.completed":
                    {
                        var s = (Stripe.Checkout.Session)evt.Data.Object;
                        if (!string.IsNullOrWhiteSpace(s.ClientReferenceId) && Guid.TryParse(s.ClientReferenceId, out _))
                        {
                            await SetUserPremiumAsync(
                                s.ClientReferenceId!,
                                s.CustomerId ?? s.Customer?.Id,
                                s.SubscriptionId ?? s.Subscription?.Id,
                                "active"
                            );
                        }
                        break;
                    }

                case "customer.subscription.created":
                case "customer.subscription.updated":
                case "customer.subscription.deleted":
                    {
                        var sub = (Subscription)evt.Data.Object;
                        await MirrorSubscriptionAsync(sub);
                        break;
                    }

                case "invoice.payment_failed":
                    {
                        var inv = (Invoice)evt.Data.Object;
                        if (!string.IsNullOrWhiteSpace(inv.CustomerId))
                            await SetPremiumStatusByCustomerAsync(inv.CustomerId, "past_due");
                        break;
                    }

                case "invoice.payment_succeeded":
                    {
                        var inv2 = (Invoice)evt.Data.Object;
                        if (!string.IsNullOrWhiteSpace(inv2.CustomerId))
                            await SetPremiumStatusByCustomerAsync(inv2.CustomerId, "active");
                        break;
                    }

                default:
                    // ignore other events
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error processing Stripe webhook {Type}", evt.Type);
        }

        return Ok();
    }

    private async Task MirrorSubscriptionAsync(Subscription sub)
    {
        if (sub is null || string.IsNullOrWhiteSpace(sub.CustomerId)) return;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == sub.CustomerId);
        if (user is null) return;

        user.StripeSubscriptionId = sub.Id;
        user.PremiumStatus = sub.Status;
        user.IsPremium = sub.Status is "active" or "trialing";
        user.PremiumCancelAtPeriodEnd = sub.CancelAtPeriodEnd;

        // NEW: current period end now lives on subscription items (choose earliest if multiple)
        DateTime? itemPeriodEnd = null;
        if (sub.Items?.Data != null && sub.Items.Data.Count > 0)
        {
            itemPeriodEnd = sub.Items.Data
                .Select(i => i.CurrentPeriodEnd)      // DateTime? on item in recent SDKs
                .Where(d => d != null)
                .OrderBy(d => d)
                .FirstOrDefault();
        }

        if (itemPeriodEnd != null)
            user.PremiumCurrentPeriodEnd = new DateTimeOffset(itemPeriodEnd.Value);

        await _db.SaveChangesAsync();
    }

    private async Task SetUserPremiumAsync(string userIdString, string customerId, string? subId, string status)
    {
        if (!Guid.TryParse(userIdString, out var userId)) return;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return;

        user.IsPremium = status is "active" or "trialing";
        user.PremiumStatus = status;
        user.PremiumSince ??= DateTimeOffset.UtcNow;
        user.StripeCustomerId = customerId;
        user.StripeSubscriptionId = subId;

        // No period end available here reliably; webhook for subscription.* will populate it.
        await _db.SaveChangesAsync();
    }

    private async Task SetPremiumStatusByCustomerAsync(string customerId, string status)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);
        if (user is null) return;

        user.PremiumStatus = status;
        user.IsPremium = status is "active" or "trialing";
        await _db.SaveChangesAsync();
    }
}
