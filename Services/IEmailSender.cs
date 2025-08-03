namespace Platerly.Api.Services
{
    public interface IEmailSender
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlContent);
        Task SendPasswordResetEmailAsync(string toEmail, string subject, string htmlContent);
    }
}
