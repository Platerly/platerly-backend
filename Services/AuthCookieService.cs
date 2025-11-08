using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;

namespace Platerly.Api.Services
{
    public class AuthCookieService
    {
        private readonly IConfiguration _config;
        private const string DefaultName = "platerly.r";

        public AuthCookieService(IConfiguration config) { _config = config; }

        private string CookieName => _config["AuthCookies:RefreshCookieName"] ?? DefaultName;
        private string? Domain => string.IsNullOrWhiteSpace(_config["AuthCookies:Domain"]) ? null : _config["AuthCookies:Domain"];
        private bool Secure
        {
            get
            {
                // Default to true (premium default). Allow opt-out via config for local dev.
                return bool.TryParse(_config["AuthCookies:Secure"], out var s) ? s : true;
            }
        }
        private SameSiteMode SameSite
        {
            get
            {
                var v = (_config["AuthCookies:SameSite"] ?? "Lax").Trim().ToLowerInvariant();
                return v switch
                {
                    "none" => SameSiteMode.None,
                    "strict" => SameSiteMode.Strict,
                    _ => SameSiteMode.Lax
                };
            }
        }

        public void SetRefreshCookie(HttpResponse response, string token, DateTime expiresUtc)
        {
            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = Secure,
                IsEssential = true,
                Expires = expiresUtc,
                Domain = Domain,
                SameSite = SameSite,
                Path = "/"
            };

            response.Cookies.Append(CookieName, token, opts);
        }

        public void ClearRefreshCookie(HttpResponse response)
        {
            // Must match Path & (optionally) Domain to delete correctly
            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = Secure,
                IsEssential = true,
                Domain = Domain,
                SameSite = SameSite,
                Path = "/"
            };

            response.Cookies.Delete(CookieName, opts);
        }

        public string? ReadRefreshCookie(HttpRequest request)
        {
            return request.Cookies.TryGetValue(CookieName, out var v) ? v : null;
        }
    }
}
