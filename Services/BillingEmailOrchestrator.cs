using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services
{
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

        public string ManageBillingUrl()
        {
            var frontendBaseUrl = _config["FrontendBaseUrl"] ?? "https://www.platerly.com";
            // Update this route to whatever you actually use:
            return $"{frontendBaseUrl}/settings/billing";
        }

        public string SupportEmail() => "support@recipzee.com";
    }
}
