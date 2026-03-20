using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;

namespace Platerly.Api.Services
{
    public class TrialEmailService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TrialEmailService> _logger;

        public TrialEmailService(IServiceScopeFactory scopeFactory, ILogger<TrialEmailService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Short startup delay — let the app fully initialise first
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SendPendingTrialEmailsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TrialEmailService: unexpected error");
                }

                await Task.Delay(TimeSpan.FromHours(2), stoppingToken);
            }
        }

        private async Task SendPendingTrialEmailsAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlaterlyDbContext>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<BillingEmailOrchestrator>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var frontendUrl = config["FrontendBaseUrl"] ?? "https://www.recipzee.com";
            var now = DateTimeOffset.UtcNow;

            var users = await db.Users
                .AsNoTracking()
                .Where(u => u.TrialStartUtc.HasValue && u.Email != null)
                .Select(u => new
                {
                    u.Id,
                    u.Email,
                    u.DisplayName,
                    u.TrialStartUtc,
                    u.TrialEndUtc,
                    u.IsPremium
                })
                .ToListAsync(ct);

            _logger.LogInformation("TrialEmailService: checking {Count} trial users", users.Count);

            foreach (var u in users)
            {
                if (string.IsNullOrWhiteSpace(u.Email)) continue;

                var start = u.TrialStartUtc!.Value;
                var end = u.TrialEndUtc;
                var name = string.IsNullOrWhiteSpace(u.DisplayName) ? "there" : u.DisplayName;

                // Welcome — first 24 hours after trial starts
                if (now >= start && now < start.AddDays(1))
                {
                    await orchestrator.TrySendOnceAsync(
                        u.Id, u.Email,
                        $"trial-welcome-{u.Id}", "trial.welcome",
                        "Welcome to your 7-day Recipzee Premium trial!",
                        BuildWelcomeEmail(name, frontendUrl));
                }

                // Day 3 reminder
                if (now >= start.AddDays(3) && now < start.AddDays(4))
                {
                    await orchestrator.TrySendOnceAsync(
                        u.Id, u.Email,
                        $"trial-day3-{u.Id}", "trial.day3",
                        "3 days in — here's what you've unlocked on Recipzee",
                        BuildDay3Email(name, frontendUrl));
                }

                // Day 6 — trial ends tomorrow
                if (now >= start.AddDays(6) && now < start.AddDays(7))
                {
                    await orchestrator.TrySendOnceAsync(
                        u.Id, u.Email,
                        $"trial-day6-{u.Id}", "trial.day6",
                        "Your Recipzee Premium trial ends tomorrow",
                        BuildDay6Email(name, frontendUrl));
                }

                // Day 8 — trial ended, not converted
                if (end.HasValue && now >= end.Value && now < end.Value.AddDays(2) && !u.IsPremium)
                {
                    await orchestrator.TrySendOnceAsync(
                        u.Id, u.Email,
                        $"trial-day8-{u.Id}", "trial.day8",
                        "Your Recipzee Premium trial has ended",
                        BuildDay8Email(name, frontendUrl));
                }
            }
        }

        // ── Email templates ──────────────────────────────────────────────────

        private static string Wrap(string name, string body, string frontendUrl) => $@"
<!DOCTYPE html>
<html lang=""en"">
<head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1""></head>
<body style=""margin:0;padding:0;background:#f4f4f5;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f4f4f5;padding:32px 16px;"">
    <tr><td align=""center"">
      <table width=""100%"" style=""max-width:560px;background:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,0.08);"">

        <!-- Header -->
        <tr>
          <td style=""background:#0f172a;padding:28px 32px;text-align:center;"">
            <span style=""font-size:22px;font-weight:800;color:#4ade80;letter-spacing:-0.5px;"">Recipzee</span>
          </td>
        </tr>

        <!-- Body -->
        <tr>
          <td style=""padding:32px;"">
            <p style=""margin:0 0 16px;font-size:15px;color:#374151;"">Hi {name},</p>
            {body}
            <p style=""margin:24px 0 0;font-size:13px;color:#9ca3af;"">
              — The Recipzee Team<br>
              <a href=""{frontendUrl}"" style=""color:#4ade80;text-decoration:none;"">recipzee.com</a>
            </p>
          </td>
        </tr>

        <!-- Footer -->
        <tr>
          <td style=""background:#f9fafb;padding:16px 32px;text-align:center;border-top:1px solid #e5e7eb;"">
            <p style=""margin:0;font-size:12px;color:#9ca3af;"">
              You're receiving this because you created a Recipzee account.<br>
              <a href=""{frontendUrl}/settings"" style=""color:#6b7280;text-decoration:underline;"">Manage preferences</a>
            </p>
          </td>
        </tr>

      </table>
    </td></tr>
  </table>
</body>
</html>";

        private static string GreenButton(string text, string url) =>
            $@"<a href=""{url}"" style=""display:inline-block;background:#4ade80;color:#0f172a;font-weight:700;font-size:15px;padding:12px 28px;border-radius:10px;text-decoration:none;margin:20px 0;"">{text}</a>";

        private static string BuildWelcomeEmail(string name, string frontendUrl)
        {
            var body = $@"
<p style=""margin:0 0 16px;font-size:15px;color:#374151;"">
  Your <strong>7-day Recipzee Premium trial</strong> is now active. Here's everything you can do:
</p>
<table style=""width:100%;border-collapse:collapse;margin:16px 0;"">
  <tr><td style=""padding:10px 0;border-bottom:1px solid #f3f4f6;font-size:14px;color:#374151;"">
    <strong style=""color:#059669;"">✓</strong>&nbsp; <strong>Unlimited allergen scans</strong> — check every recipe for your allergens instantly
  </td></tr>
  <tr><td style=""padding:10px 0;border-bottom:1px solid #f3f4f6;font-size:14px;color:#374151;"">
    <strong style=""color:#059669;"">✓</strong>&nbsp; <strong>Hide my allergens filter</strong> — browse the feed without seeing unsafe recipes
  </td></tr>
  <tr><td style=""padding:10px 0;border-bottom:1px solid #f3f4f6;font-size:14px;color:#374151;"">
    <strong style=""color:#059669;"">✓</strong>&nbsp; <strong>Recipe remixing</strong> — put your own spin on any recipe
  </td></tr>
  <tr><td style=""padding:10px 0;font-size:14px;color:#374151;"">
    <strong style=""color:#059669;"">✓</strong>&nbsp; <strong>Trending ingredients</strong> — see what the community is cooking right now
  </td></tr>
</table>
<p style=""margin:0 0 4px;font-size:14px;color:#6b7280;"">
  Your trial ends in 7 days. After that it's just <strong>$6.99/month</strong> to keep everything.
</p>
{GreenButton("Start Exploring", frontendUrl + "/feed")}";

            return Wrap(name, body, frontendUrl);
        }

        private static string BuildDay3Email(string name, string frontendUrl)
        {
            var body = $@"
<p style=""margin:0 0 16px;font-size:15px;color:#374151;"">
  You're 3 days into your Premium trial — 4 days left to make the most of it.
</p>
<p style=""margin:0 0 16px;font-size:15px;color:#374151;"">
  Here are a few things worth trying before your trial ends:
</p>
<table style=""width:100%;border-collapse:collapse;margin:16px 0;"">
  <tr><td style=""padding:10px 0;border-bottom:1px solid #f3f4f6;font-size:14px;color:#374151;"">
    <strong>1.</strong> Open any recipe and click an ingredient — see safe alternatives
  </td></tr>
  <tr><td style=""padding:10px 0;border-bottom:1px solid #f3f4f6;font-size:14px;color:#374151;"">
    <strong>2.</strong> Turn on <em>Hide my allergens</em> in the feed filter — browse with peace of mind
  </td></tr>
  <tr><td style=""padding:10px 0;font-size:14px;color:#374151;"">
    <strong>3.</strong> Remix a recipe you love and make it your own
  </td></tr>
</table>
{GreenButton("Go to Recipzee", frontendUrl + "/feed")}
<p style=""margin:0;font-size:13px;color:#9ca3af;"">
  Continuing after your trial is <strong>$6.99/month</strong>. Cancel anytime.
</p>";

            return Wrap(name, body, frontendUrl);
        }

        private static string BuildDay6Email(string name, string frontendUrl)
        {
            var body = $@"
<p style=""margin:0 0 16px;font-size:15px;color:#374151;"">
  Your Recipzee Premium trial ends <strong>tomorrow</strong>.
</p>
<p style=""margin:0 0 16px;font-size:15px;color:#374151;"">
  After your trial ends you'll drop back to:
</p>
<table style=""width:100%;border-collapse:collapse;margin:16px 0;"">
  <tr><td style=""padding:10px 0;border-bottom:1px solid #f3f4f6;font-size:14px;color:#374151;"">
    <strong style=""color:#ef4444;"">✗</strong>&nbsp; 3 allergen scans per week (instead of unlimited)
  </td></tr>
  <tr><td style=""padding:10px 0;border-bottom:1px solid #f3f4f6;font-size:14px;color:#374151;"">
    <strong style=""color:#ef4444;"">✗</strong>&nbsp; No allergen feed filter
  </td></tr>
  <tr><td style=""padding:10px 0;font-size:14px;color:#374151;"">
    <strong style=""color:#ef4444;"">✗</strong>&nbsp; No recipe remixing
  </td></tr>
</table>
<p style=""margin:0 0 4px;font-size:14px;color:#374151;"">
  Upgrade now to keep full access for just <strong>$6.99/month</strong>.
</p>
{GreenButton("Upgrade for $6.99/mo", frontendUrl + "/settings?tab=subscription")}
<p style=""margin:0;font-size:13px;color:#9ca3af;"">Cancel anytime. No hidden fees.</p>";

            return Wrap(name, body, frontendUrl);
        }

        private static string BuildDay8Email(string name, string frontendUrl)
        {
            var body = $@"
<p style=""margin:0 0 16px;font-size:15px;color:#374151;"">
  Your 7-day Premium trial has ended. You're back on the free plan.
</p>
<p style=""margin:0 0 16px;font-size:15px;color:#374151;"">
  You now have <strong>3 allergen scans per week</strong>. Allergen feed filtering, recipe remixing, and trending ingredients are no longer available.
</p>
<p style=""margin:0 0 4px;font-size:15px;color:#374151;"">
  Upgrade to keep everything for <strong>$6.99/month</strong>.
</p>
{GreenButton("Upgrade Now", frontendUrl + "/settings?tab=subscription")}
<p style=""margin:0;font-size:13px;color:#9ca3af;"">Cancel anytime. Takes 30 seconds.</p>";

            return Wrap(name, body, frontendUrl);
        }

        public static string BuildWelcomePremiumEmail(string name, string frontendUrl)
        {
            var body = $@"
<p style=""margin:0 0 16px;font-size:15px;color:#374151;"">
  You're now a Recipzee Premium member. Thank you for your support!
</p>
<table style=""width:100%;border-collapse:collapse;margin:16px 0;"">
  <tr><td style=""padding:10px 0;border-bottom:1px solid #f3f4f6;font-size:14px;color:#374151;"">
    <strong style=""color:#059669;"">✓</strong>&nbsp; Unlimited allergen scans on every recipe
  </td></tr>
  <tr><td style=""padding:10px 0;border-bottom:1px solid #f3f4f6;font-size:14px;color:#374151;"">
    <strong style=""color:#059669;"">✓</strong>&nbsp; Hide my allergens feed filter
  </td></tr>
  <tr><td style=""padding:10px 0;border-bottom:1px solid #f3f4f6;font-size:14px;color:#374151;"">
    <strong style=""color:#059669;"">✓</strong>&nbsp; Unlimited recipe remixing
  </td></tr>
  <tr><td style=""padding:10px 0;font-size:14px;color:#374151;"">
    <strong style=""color:#059669;"">✓</strong>&nbsp; Trending ingredients access
  </td></tr>
</table>
<p style=""margin:0 0 4px;font-size:14px;color:#6b7280;"">
  You can manage your subscription anytime from your settings.
</p>
{GreenButton("Go to Recipzee", frontendUrl + "/feed")}";

            return Wrap(name, body, frontendUrl);
        }
    }
}
