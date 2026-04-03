using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services
{
    /// <summary>
    /// Infrastructure service — orchestrates transactional billing emails (subscription start,
    /// cancellation, renewal, payment failure) triggered by Stripe webhook events.
    /// Depends on <see cref="PlaterlyDbContext"/> and <see cref="IEmailSender"/>.
    ///
    /// Idempotency: <see cref="TrySendOnceAsync"/> checks for an existing <c>BillingEmailEvent</c>
    /// row keyed on <c>(StripeEventId, EventType)</c> before sending. After a successful send it
    /// writes the record; a <see cref="DbUpdateException"/> from the unique index (race condition with
    /// a concurrent webhook worker) is silently swallowed — the email was already sent.
    /// </summary>
    public class BillingEmailOrchestrator
    {
        private readonly PlaterlyDbContext _db;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;

        public BillingEmailOrchestrator(PlaterlyDbContext db, IEmailSender emailSender, IConfiguration config)
        {
            _db = db;
            _emailSender = emailSender;
            _config = config;
        }

        /// <summary>
        /// Sends a billing email exactly once per <c>(stripeEventId, eventType)</c> pair.
        /// Skips immediately if the event was already sent (idempotent check).
        /// Sends the email first; only records the event row if the send succeeds, so a failed
        /// send will be retried on the next webhook delivery attempt.
        /// </summary>
        /// <param name="userId">The user the billing event relates to (stored for auditing).</param>
        /// <param name="toEmail">The recipient email address.</param>
        /// <param name="stripeEventId">The Stripe event ID — uniqueness key for deduplication.</param>
        /// <param name="eventType">The event type string — combined with <paramref name="stripeEventId"/> for the unique index.</param>
        /// <param name="subject">Email subject line.</param>
        /// <param name="htmlBody">HTML email body.</param>
        public async Task TrySendOnceAsync(
            Guid userId,
            string toEmail,
            string stripeEventId,
            string eventType,
            string subject,
            string htmlBody)
        {
            // Fast skip (most common case on retries)
            var exists = await _db.BillingEmailEvents
                .AnyAsync(x => x.StripeEventId == stripeEventId && x.EventType == eventType);

            if (exists) return;

            // Attempt send first (so we don't record if email fails)
            await _emailSender.SendEmailAsync(toEmail, subject, htmlBody);

            // Record, protected by unique index (race-safe)
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
                // If two webhook workers raced, unique index wins: treat as already sent.
            }
        }

        /// <summary>Returns the absolute URL to the user-facing billing settings page.</summary>
        public string ManageBillingUrl()
        {
            var frontendBaseUrl = _config["FrontendBaseUrl"] ?? "https://www.recipzee.com";
            // Update this route to whatever you actually use:
            return $"{frontendBaseUrl}/settings/billing";
        }

        /// <summary>Returns the canonical Recipzee support email address for inclusion in billing emails.</summary>
        public string SupportEmail() => "support@recipzee.com";
    }
}
