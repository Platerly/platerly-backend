using Microsoft.Extensions.Options;
using Platerly.Api.Models;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Platerly.Api.Services
{
    public class SendGridEmailSender: IEmailSender
    {
        private readonly EmailSettings _emailSettings;

        public SendGridEmailSender(IOptions<EmailSettings> emailSettings)
        {
            _emailSettings = emailSettings.Value;
        }

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

        public async Task SendPasswordResetEmailAsync(string toEmail,string userName, string resetLink)
        {
            var subject = "Reset your Platerly password";
            var html = $@"
        <h2>Reset Your Password</h2>
        <p>We received a request to reset your password. Click the link below to choose a new one. This link is valid for 1 hour.</p>
        <a href=""{resetLink}"">{resetLink}</a>
        <p>If you did not request a password reset, you can safely ignore this email.</p>";

            await SendEmailAsync(toEmail, subject, html);
        }

    }
}
