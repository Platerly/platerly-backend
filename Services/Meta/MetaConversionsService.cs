using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Platerly.Api.Services.Meta;

public interface IMetaConversionsService
{
    Task TrackRegistrationAsync(string email, string? ipAddress = null, string? userAgent = null);
    Task TrackEventAsync(string eventName, string? email = null, string? ipAddress = null, string? userAgent = null, Dictionary<string, object>? customData = null);
}

/// <summary>
/// Infrastructure service — sends server-side conversion events to the Meta (Facebook)
/// Conversions API (CAPI) using Graph API v21.0.
/// Requires <c>Meta:PixelId</c> and <c>Meta:AccessToken</c> configuration values;
/// if either is missing the service disables itself with a warning log.
///
/// PII handling: email addresses are SHA-256-hashed (lowercase-trimmed) before transmission
/// to comply with Meta CAPI data normalisation requirements. IP address and User-Agent are
/// sent as plain text for browser-side deduplication.
/// </summary>
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

    /// <summary>
    /// Fires a <c>CompleteRegistration</c> CAPI event. Delegates to <see cref="TrackEventAsync"/>
    /// with a USD currency and value=0 custom data payload.
    /// </summary>
    /// <param name="email">The registering user's email (will be SHA-256-hashed before transmission).</param>
    /// <param name="ipAddress">Optional: client IP address for deduplication.</param>
    /// <param name="userAgent">Optional: browser User-Agent for deduplication.</param>
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

    /// <summary>
    /// Sends an arbitrary named event to the Meta CAPI. Constructs a JSON payload matching
    /// the Graph API <c>/{pixelId}/events</c> format and POST-requests it via <see cref="HttpClient"/>.
    /// Exceptions are caught and logged at Error level — tracking failures never propagate to callers.
    /// </summary>
    /// <param name="eventName">Meta standard or custom event name (e.g. "CompleteRegistration", "Purchase").</param>
    /// <param name="email">Optional email address (hashed with SHA-256 before sending).</param>
    /// <param name="ipAddress">Optional client IP for server-side deduplication.</param>
    /// <param name="userAgent">Optional User-Agent for deduplication.</param>
    /// <param name="customData">Optional additional event data (currency, value, content_ids, etc.).</param>
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
                ["event_source_url"] = "https://www.recipzee.com",
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
