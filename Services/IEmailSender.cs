namespace Platerly.Api.Services
{
    /// <summary>Sends transactional emails; implemented by <c>SendGridEmailSender</c>.</summary>
    public interface IEmailSender
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlContent);
        Task SendPasswordResetEmailAsync(string toEmail, string subject, string htmlContent);
    }
}
