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

        // Keep the configurable name (fallback to default)
        private string CookieName => _config["AuthCookies:RefreshCookieName"] ?? DefaultName;

        public void SetRefreshCookie(HttpResponse response, string token, DateTime expiresUtc)
        {
            // Use the minimal, known-good set that worked in debug:
            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,                    // force HTTPS only
                SameSite = SameSiteMode.None,       // allow cross-site
                IsEssential = true,
                Expires = expiresUtc,
                Path = "/"
                // Domain intentionally omitted (host-only); this matched your successful debug cookie
            };

            // Write the cookie
            response.Cookies.Append(CookieName, token, opts);

            // TEMP: add a header so we can see in PowerShell/Network that we attempted to set it
            // (Remove this header once we verify everything)
            response.Headers["X-Refresh-Cookie-Attempt"] =
                $"{CookieName}|SameSite={opts.SameSite}|Secure={opts.Secure}|Domain={(opts.Domain ?? "<host>")}|Expires={opts.Expires:o}";
        }

        public void ClearRefreshCookie(HttpResponse response)
        {
            // Must match Path; omit Domain for host-only delete
            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                IsEssential = true,
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
