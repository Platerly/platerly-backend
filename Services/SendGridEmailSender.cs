using Microsoft.Extensions.Options;
using Platerly.Api.Models;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Platerly.Api.Services
{
    /// <summary>
    /// Infrastructure service — sends transactional emails via the SendGrid API.
    /// Implements <see cref="IEmailSender"/>. Configuration is bound from <c>EmailSettings</c>
    /// (<c>ApiKey</c>, <c>SenderEmail</c>, <c>SenderName</c>).
    ///
    /// Throws <see cref="ApplicationException"/> when SendGrid returns HTTP 4xx or 5xx,
    /// so callers should handle email failures gracefully at the orchestration layer.
    /// </summary>
    public class SendGridEmailSender: IEmailSender
    {
        private readonly EmailSettings _emailSettings;

        public SendGridEmailSender(IOptions<EmailSettings> emailSettings)
        {
            _emailSettings = emailSettings.Value;
        }

        /// <summary>
        /// Sends a transactional email via SendGrid. Throws <see cref="ApplicationException"/>
        /// if SendGrid returns a non-2xx status code.
        /// </summary>
        /// <param name="toEmail">Recipient email address.</param>
        /// <param name="subject">Email subject line.</param>
        /// <param name="htmlContent">Full HTML email body.</param>
        public async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            var client = new SendGridClient(_emailSettings.ApiKey);
            var from = new EmailAddress(_emailSettings.SenderEmail, _emailSettings.SenderName);
            var to = new EmailAddress(toEmail);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent: null, htmlContent: htmlContent);

            var response = await client.SendEmailAsync(msg);
            if ((int)response.StatusCode >= 400)
            {
                var body = await response.Body.ReadAsStringAsync();
                throw new ApplicationException($"SendGrid failed: {response.StatusCode},body: {body}");
            }
        }

        /// <summary>
        /// Sends a password-reset email containing a time-limited link (valid 1 hour).
        /// Delegates to <see cref="SendEmailAsync"/> with a pre-built HTML template.
        /// </summary>
        /// <param name="toEmail">Recipient email address.</param>
        /// <param name="userName">The user's display name (not currently used in the template but reserved).</param>
        /// <param name="resetLink">The full absolute URL of the password-reset link.</param>
        public async Task SendPasswordResetEmailAsync(string toEmail,string userName, string resetLink)
        {
            var subject = "Reset your Recipzee password";
            var html = $@"
        <h2>Reset Your Password</h2>
        <p>We received a request to reset your password. Click the link below to choose a new one. This link is valid for 1 hour.</p>
        <a href=""{resetLink}"">{resetLink}</a>
        <p>If you did not request a password reset, you can safely ignore this email.</p>";

            await SendEmailAsync(toEmail, subject, html);
        }

    }
}
