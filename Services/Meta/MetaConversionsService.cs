using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Platerly.Api.Services.Meta;

public interface IMetaConversionsService
{
    Task TrackRegistrationAsync(string email, string? ipAddress = null, string? userAgent = null);
    Task TrackEventAsync(string eventName, string? email = null, string? ipAddress = null, string? userAgent = null, Dictionary<string, object>? customData = null);
}

public class MetaConversionsService : IMetaConversionsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MetaConversionsService> _logger;
    private readonly string _pixelId;
    private readonly string _accessToken;
    private readonly bool _isEnabled;

    public MetaConversionsService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<MetaConversionsService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _pixelId = configuration["Meta:PixelId"] ?? "";
        _accessToken = configuration["Meta:AccessToken"] ?? "";
        _isEnabled = !string.IsNullOrEmpty(_pixelId) && !string.IsNullOrEmpty(_accessToken);

        if (!_isEnabled)
        {
            _logger.LogWarning("Meta Conversions API is disabled - PixelId or AccessToken not configured");
        }
    }

    public async Task TrackRegistrationAsync(string email, string? ipAddress = null, string? userAgent = null)
    {
        await TrackEventAsync(
            "CompleteRegistration",
            email,
            ipAddress,
            userAgent,
            new Dictionary<string, object>
            {
                ["currency"] = "USD",
                ["value"] = 0
            });
    }

    public async Task TrackEventAsync(
        string eventName,
        string? email = null,
        string? ipAddress = null,
        string? userAgent = null,
        Dictionary<string, object>? customData = null)
    {
        if (!_isEnabled)
        {
            _logger.LogDebug("Meta Conversions API disabled, skipping event: {EventName}", eventName);
            return;
        }

        try
        {
            var eventTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var eventId = Guid.NewGuid().ToString();

            var userData = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(email))
            {
                userData["em"] = HashSha256(email.ToLowerInvariant().Trim());
            }

            if (!string.IsNullOrEmpty(ipAddress))
            {
                userData["client_ip_address"] = ipAddress;
            }

            if (!string.IsNullOrEmpty(userAgent))
            {
                userData["client_user_agent"] = userAgent;
            }

            var eventData = new Dictionary<string, object>
            {
                ["event_name"] = eventName,
                ["event_time"] = eventTime,
                ["event_id"] = eventId,
                ["event_source_url"] = "https://www.platerly.com",
                ["action_source"] = "website",
                ["user_data"] = userData
            };

            if (customData != null && customData.Count > 0)
            {
                eventData["custom_data"] = customData;
            }

            var payload = new Dictionary<string, object>
            {
                ["data"] = new[] { eventData }
            };

            var url = $"https://graph.facebook.com/v21.0/{_pixelId}/events?access_token={_accessToken}";

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Meta CAPI event sent: {EventName}, EventId: {EventId}", eventName, eventId);
            }
            else
            {
                _logger.LogError("Meta CAPI failed: {StatusCode} - {Response}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            // Don't let tracking failures break the app
            _logger.LogError(ex, "Meta CAPI error for event: {EventName}", eventName);
        }
    }

    private static string HashSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
