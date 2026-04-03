namespace Platerly.Api.Models;

/// <summary>
/// Represents a business (B2B) account that subscribes to the Recipzee API.
///
/// One B2B account per user (1-to-1 with <see cref="User"/>). The owning user
/// authenticates via JWT to manage billing; API clients authenticate via API key
/// for data endpoints — these are separate concerns.
///
/// Stripe fields mirror the same shape as the B2C <see cref="User"/> billing fields
/// so <c>MirrorB2BSubscription()</c> in the billing controller can apply the same
/// reflection-based helpers to normalise SDK version differences.
/// </summary>
public class B2BAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user who owns and manages this business account.</summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string? CompanyName { get; set; }

    /// <summary>
    /// Billing contact email — may differ from the owner's login email.
    /// Falls back to <c>User.Email</c> when null.
    /// </summary>
    public string? BillingEmail { get; set; }

    // ── Stripe ────────────────────────────────────────────────────────────────
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    /// <summary>Raw Stripe subscription status: active, past_due, canceled, etc.</summary>
    public string? PlanStatus { get; set; }

    /// <summary>
    /// Plan tier slug — one of: starter, professional, enterprise.
    /// Derived from the Stripe price ID in the webhook handler.
    /// </summary>
    public string? PlanTier { get; set; }

    /// <summary>True when PlanStatus is "active" or "trialing".</summary>
    public bool IsActive { get; set; }

    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>UTC end of the current billing period — used for "Renews / Ends on" UI.</summary>
    public DateTimeOffset? CurrentPeriodEnd { get; set; }

    /// <summary>Set once when a paid subscription first activates; cleared on deactivation.</summary>
    public DateTimeOffset? SubscribedSince { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
