using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;

namespace Platerly.Api.Services
{
    public class AuthCookieService
    {
        private readonly IConfiguration _config;
        private const string DefaultRefreshName = "platerly.r";
        private const string DefaultAccessName = "platerly.a";

        public AuthCookieService(IConfiguration config) { _config = config; }

        // Configurable names, with sensible defaults
        private string RefreshCookieName => _config["AuthCookies:RefreshCookieName"] ?? DefaultRefreshName;
        private string AccessCookieName => _config["AuthCookies:AccessCookieName"] ?? DefaultAccessName;

        /* ───────────── Refresh cookie ───────────── */

        public void SetRefreshCookie(HttpResponse response, string token, DateTime expiresUtc)
        {
            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,                    // force HTTPS only
                SameSite = SameSiteMode.None,     // allow cross-site
                IsEssential = true,
                Expires = expiresUtc,
                Path = "/"
                // Domain intentionally omitted (host-only)
            };

            response.Cookies.Append(RefreshCookieName, token, opts);

            response.Headers["X-Refresh-Cookie-Attempt"] =
                $"{RefreshCookieName}|SameSite={opts.SameSite}|Secure={opts.Secure}|Domain={(opts.Domain ?? "<host>")}|Expires={opts.Expires:o}";
        }

        public void ClearRefreshCookie(HttpResponse response)
        {
            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                IsEssential = true,
                Path = "/"
            };

            response.Cookies.Delete(RefreshCookieName, opts);
        }

        public string? ReadRefreshCookie(HttpRequest request)
        {
            return request.Cookies.TryGetValue(RefreshCookieName, out var v) ? v : null;
        }

        /* ───────────── Access-token cookie ───────────── */

        public void SetAccessCookie(HttpResponse response, string token, DateTime expiresUtc)
        {
            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                IsEssential = true,
                Expires = expiresUtc,
                Path = "/"
            };

            response.Cookies.Append(AccessCookieName, token, opts);
        }

        public void ClearAccessCookie(HttpResponse response)
        {
            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                IsEssential = true,
                Path = "/"
            };

            response.Cookies.Delete(AccessCookieName, opts);
        }

        public string? ReadAccessCookie(HttpRequest request)
        {
            return request.Cookies.TryGetValue(AccessCookieName, out var v) ? v : null;
        }
    }
}
