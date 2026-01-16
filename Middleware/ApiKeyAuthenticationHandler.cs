using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Platerly.Api.Services.ApiKeys;

namespace Platerly.Api.Middleware
{
    public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        public const string DefaultScheme = "ApiKey";
        public const string HeaderName = "X-API-Key";
    }

    public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
    {
        private readonly IApiKeyService _apiKeyService;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IApiKeyService apiKeyService)
            : base(options, logger, encoder)
        {
            _apiKeyService = apiKeyService;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Check for API key in header
            if (!Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out var apiKeyHeader))
            {
                // Also check Authorization header with "ApiKey" scheme
                if (Request.Headers.TryGetValue("Authorization", out var authHeader))
                {
                    var authValue = authHeader.ToString();
                    if (authValue.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
                    {
                        apiKeyHeader = authValue.Substring(7);
                    }
                    else
                    {
                        return AuthenticateResult.NoResult();
                    }
                }
                else
                {
                    return AuthenticateResult.NoResult();
                }
            }

            var rawKey = apiKeyHeader.ToString();
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                return AuthenticateResult.Fail("API key is empty");
            }

            var apiKey = await _apiKeyService.ValidateApiKeyAsync(rawKey);
            if (apiKey == null)
            {
                return AuthenticateResult.Fail("Invalid or expired API key");
            }

            // Check IP whitelist if configured
            if (!string.IsNullOrWhiteSpace(apiKey.IpWhitelist))
            {
                var clientIp = Context.Connection.RemoteIpAddress?.ToString();
                var allowedIps = apiKey.IpWhitelist.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (clientIp != null && !allowedIps.Contains(clientIp))
                {
                    Logger.LogWarning("API key {KeyId} used from non-whitelisted IP {IP}", apiKey.Id, clientIp);
                    return AuthenticateResult.Fail("IP address not allowed");
                }
            }

            // Create claims
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, apiKey.UserId.ToString()),
                new Claim("ApiKeyId", apiKey.Id.ToString()),
                new Claim("ApiKeyName", apiKey.Name),
                new Claim("ApiKeyRateLimit", apiKey.RateLimitPerMinute.ToString()),
            };

            // Add scope claims
            if (!string.IsNullOrWhiteSpace(apiKey.Scopes))
            {
                foreach (var scope in apiKey.Scopes.Split(','))
                {
                    claims.Add(new Claim("ApiKeyScope", scope.Trim()));
                }
            }

            var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationOptions.DefaultScheme);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationOptions.DefaultScheme);

            return AuthenticateResult.Success(ticket);
        }
    }

    public static class ApiKeyAuthenticationExtensions
    {
        public static AuthenticationBuilder AddApiKeyAuthentication(
            this AuthenticationBuilder builder,
            Action<ApiKeyAuthenticationOptions>? configureOptions = null)
        {
            return builder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationOptions.DefaultScheme,
                configureOptions ?? (_ => { }));
        }
    }
}
