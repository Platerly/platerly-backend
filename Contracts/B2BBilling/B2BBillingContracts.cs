namespace Platerly.Api.Contracts.B2BBilling;

// ── Requests ─────────────────────────────────────────────────────────────────

/// <summary>
/// Initiates a Stripe Checkout Session for a B2B plan.
/// <c>UserId</c> must match the JWT subject; <c>PriceId</c> must be one of the
/// configured <c>StripeB2B:PriceId*</c> values.
/// </summary>
public record CreateB2BCheckoutRequest(string UserId, string PriceId);

/// <summary>Opens the Stripe Customer Portal for self-serve B2B plan management.</summary>
public record CreateB2BPortalRequest(string UserId);

/// <summary>Schedules the B2B subscription to cancel at end of current period.</summary>
public record CancelB2BSubscriptionRequest(string UserId);

/// <summary>Removes a pending B2B subscription cancellation.</summary>
public record ResumeB2BSubscriptionRequest(string UserId);

/// <summary>Updates the company name or billing email on the B2B account.</summary>
public record UpdateB2BAccountRequest(string UserId, string? CompanyName, string? BillingEmail);

// ── Responses ────────────────────────────────────────────────────────────────

/// <summary>
/// Returned by <c>GET /api/b2b-billing/status</c>.
/// Mirrors the shape of the B2C billing status so the frontend hook pattern is identical.
/// </summary>
public record B2BStatusResponse(
    bool IsActive,
    string? PlanTier,
    string? PlanStatus,
    string? StripeCustomerId,
    string? StripeSubscriptionId,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? SubscribedSince,
    string? CompanyName,
    string? BillingEmail
);

/// <summary>Lightweight invoice record returned by <c>GET /api/b2b-billing/invoices</c>.</summary>
public record B2BInvoiceResponse(
    string InvoiceId,
    string? Number,
    long AmountPaid,
    string Currency,
    string Status,
    DateTimeOffset Created,
    string? PdfUrl,
    string? HostedUrl
);
