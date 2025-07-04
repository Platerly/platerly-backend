using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Services;

[ApiController]
[Route("api/[controller]")]
public class TestEmailController : ControllerBase
{
    private readonly IEmailSender _emailSender;

    public TestEmailController(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest request)
    {
        try
        {
            string subject = "🧪 Platerly Test Email";
            string body = $"<h2>Hello, {request.Name}!</h2><p>This is a test email from Platerly. If you see this, SendGrid is working!</p>";

            await _emailSender.SendEmailAsync(request.Email, subject, body);

            return Ok("Email sent successfully.");
        }
        catch (Exception ex)
        {
            return BadRequest($"Error sending email: {ex.Message}");
        }
    }
}

public class TestEmailRequest
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
