using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;
using Platerly.Api.Services;
using Stripe;

namespace Platerly.Api.Controllers;

[ApiController]
[Route("stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly IConfiguration _cfg;
    private readonly PlaterlyDbContext _db;
    private readonly ILogger<StripeWebhookController> _log;
    private readonly IEmailSender _emailSender;

    public StripeWebhookController(
        IConfiguration cfg,
        PlaterlyDbContext db,
        ILogger<StripeWebhookController> log,
        IEmailSender emailSender)
    {
        _cfg = cfg;
        _db = db;
        _log = log;
        _emailSender = emailSender;
    }

    [HttpPost("webhook")]
    [IgnoreAntiforgeryToken]
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

                        // Only subscription checkout should trigger confirmation email
                        var mode = s.Mode ?? string.Empty;
                        if (!mode.Equals("subscription", StringComparison.OrdinalIgnoreCase))
                            break;

                        if (!string.IsNullOrWhiteSpace(s.ClientReferenceId) && Guid.TryParse(s.ClientReferenceId, out _))
                        {
                            await SetUserPremiumAsync(
                                s.ClientReferenceId!,
                                s.CustomerId ?? s.Customer?.Id,
                                s.SubscriptionId ?? s.Subscription?.Id,
                                "active"
                            );

                            // Send subscription confirmed email (idempotent)
                            var userId = Guid.Parse(s.ClientReferenceId!);
                            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
                            if (user is not null)
                            {
                                var toEmail = GetBillingToEmail(user);
                                if (!string.IsNullOrWhiteSpace(toEmail))
                                {
                                    await TrySendBillingEmailOnceAsync(
                                        user.Id,
                                        toEmail!,
                                        evt.Id,
                                        "SubscriptionActivated",
                                        "Your Platerly Premium is active ✅",
                                        SubscriptionConfirmedBody(ManageBillingUrl(), SupportEmail())
                                    );
                                }
                            }
                        }

                        break;
                    }

                case "customer.subscription.created":
                case "customer.subscription.updated":
                case "customer.subscription.deleted":
                    {
                        var sub = (Subscription)evt.Data.Object;

                        // Mirror to DB (existing behavior)
                        await MirrorSubscriptionAsync(sub);

                        // Email logic
                        if (sub is null || string.IsNullOrWhiteSpace(sub.CustomerId))
                            break;

                        var user = await _db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == sub.CustomerId);
                        if (user is null)
                            break;

                        var toEmail = GetBillingToEmail(user);
                        if (string.IsNullOrWhiteSpace(toEmail))
                            break;

                        var endUtc = GetSubscriptionPeriodEndUtc(sub) ?? DateTime.UtcNow;

                        // Cancel at period end email
                        if (evt.Type == "customer.subscription.updated" && sub.CancelAtPeriodEnd == true)
                        {
                            await TrySendBillingEmailOnceAsync(
                                user.Id,
                                toEmail!,
                                evt.Id,
                                "SubscriptionCancelAtPeriodEnd",
                                "Your Platerly Premium has been cancelled",
                                SubscriptionCancelledBody(endUtc, ManageBillingUrl(), SupportEmail())
                            );
                        }

                        // Subscription deleted email (ended)
                        if (evt.Type == "customer.subscription.deleted")
                        {
                            await TrySendBillingEmailOnceAsync(
                                user.Id,
                                toEmail!,
                                evt.Id,
                                "SubscriptionDeleted",
                                "Your Platerly Premium has been cancelled",
                                SubscriptionCancelledBody(endUtc, ManageBillingUrl(), SupportEmail())
                            );
                        }

                        break;
                    }

                case "invoice.payment_failed":
                    {
                        var inv = (Invoice)evt.Data.Object;

                        if (!string.IsNullOrWhiteSpace(inv.CustomerId))
                        {
                            await SetPremiumStatusByCustomerAsync(inv.CustomerId, "past_due");

                            // Payment failed email (idempotent)
                            var user = await _db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == inv.CustomerId);
                            if (user is not null)
                            {
                                var toEmail = GetBillingToEmail(user);
                                if (!string.IsNullOrWhiteSpace(toEmail))
                                {
                                    await TrySendBillingEmailOnceAsync(
                                        user.Id,
                                        toEmail!,
                                        evt.Id,
                                        "PaymentFailed",
                                        "Payment failed — update your card to keep Premium",
                                        PaymentFailedBody(ManageBillingUrl(), SupportEmail())
                                    );
                                }
                            }
                        }

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

        // Current period end lives on subscription items in your Stripe.NET version
        var itemPeriodEnd = GetSubscriptionPeriodEndUtc(sub);
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

    // -------------------------
    // Idempotent email sender
    // -------------------------
    private async Task TrySendBillingEmailOnceAsync(
        Guid userId,
        string toEmail,
        string stripeEventId,
        string eventType,
        string subject,
        string htmlBody)
    {
        var exists = await _db.BillingEmailEvents
            .AnyAsync(x => x.StripeEventId == stripeEventId && x.EventType == eventType);

        if (exists) return;

        await _emailSender.SendEmailAsync(toEmail, subject, htmlBody);

        _db.BillingEmailEvents.Add(new BillingEmailEvent
        {
            UserId = userId,
            StripeEventId = stripeEventId,
            EventType = eventType,
            SentAtUtc = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index caught a race/duplicate; treat as already sent.
        }
    }

    private static string? GetBillingToEmail(User user)
    {
        var email = !string.IsNullOrWhiteSpace(user.BillingEmail) ? user.BillingEmail : user.Email;
        return string.IsNullOrWhiteSpace(email) ? null : email;
    }

    private static DateTime? GetSubscriptionPeriodEndUtc(Subscription sub)
    {
        if (sub?.Items?.Data == null || sub.Items.Data.Count == 0)
            return null;

        var itemPeriodEnd = sub.Items.Data
            .Select(i => i.CurrentPeriodEnd) // DateTime?
            .Where(d => d != null)
            .OrderBy(d => d)
            .FirstOrDefault();

        return itemPeriodEnd.ToUniversalTime();
    }

    // -------------------------
    // URLs + templates
    // -------------------------
    private string FrontendBaseUrl() => _cfg["FrontendBaseUrl"] ?? "https://www.platerly.com";

    private string ManageBillingUrl()
    {
        // Update if your route differs
        return $"{FrontendBaseUrl()}/settings/billing";
    }

    private static string SupportEmail() => "support@platerly.com";

    private static string SubscriptionConfirmedBody(string manageUrl, string supportEmail) =>
$@"
<div style=""font-family:Arial,sans-serif;line-height:1.5"">
  <h2>Your Platerly Premium is active ✅</h2>
  <p>Thanks for subscribing — Premium is now active on your account.</p>
  <p><a href=""{manageUrl}"">Manage billing</a></p>
  <p>If you need help, contact us at <a href=""mailto:{supportEmail}"">{supportEmail}</a>.</p>
</div>";

    private static string SubscriptionCancelledBody(DateTime endDateUtc, string manageUrl, string supportEmail) =>
$@"
<div style=""font-family:Arial,sans-serif;line-height:1.5"">
  <h2>Your Platerly Premium has been cancelled</h2>
  <p>Your Premium access will remain active until <b>{endDateUtc:MMMM d, yyyy}</b>.</p>
  <p><a href=""{manageUrl}"">Manage billing</a></p>
  <p>If you need help, contact us at <a href=""mailto:{supportEmail}"">{supportEmail}</a>.</p>
</div>";

    private static string PaymentFailedBody(string manageUrl, string supportEmail) =>
$@"
<div style=""font-family:Arial,sans-serif;line-height:1.5"">
  <h2>Payment failed — action needed</h2>
  <p>We couldn’t process your payment. To keep Premium active, please update your card.</p>
  <p><a href=""{manageUrl}"">Update payment method</a></p>
  <p>If you need help, contact us at <a href=""mailto:{supportEmail}"">{supportEmail}</a>.</p>
</div>";
}
