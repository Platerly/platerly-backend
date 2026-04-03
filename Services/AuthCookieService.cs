using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;

namespace Platerly.Api.Services
{
    /// <summary>
    /// Infrastructure service — manages HttpOnly, Secure, SameSite=None authentication cookies
    /// for browser-based clients. Cookie names are configurable via <c>AuthCookies:RefreshCookieName</c>
    /// and <c>AuthCookies:AccessCookieName</c> (defaults: <c>Recipzee.r</c> and <c>recipzee.a</c>).
    ///
    /// Both cookies use:
    ///   <c>HttpOnly=true</c> — prevents JavaScript access (XSS protection).
    ///   <c>Secure=true</c> — HTTPS-only transmission.
    ///   <c>SameSite=None</c> — required for cross-site use (frontend on different origin from API).
    ///   <c>Path=/</c> — available on all paths.
    ///   No <c>Domain</c> — host-only binding for tightest scope.
    ///
    /// The access-token cookie is a session cookie when <c>expiresUtc</c> is null.
    /// </summary>
    public class AuthCookieService
    {
        private readonly IConfiguration _config;
        private const string DefaultRefreshName = "Recipzee.r";
        private const string DefaultAccessName = "recipzee.a";

        public AuthCookieService(IConfiguration config) { _config = config; }

        // Configurable names, with sensible defaults
        private string RefreshCookieName => _config["AuthCookies:RefreshCookieName"] ?? DefaultRefreshName;
        private string AccessCookieName => _config["AuthCookies:AccessCookieName"] ?? DefaultAccessName;

        /* ───────────── Refresh cookie ───────────── */

        /// <summary>
        /// Appends the refresh-token cookie to the response. Also writes an
        /// <c>X-Refresh-Cookie-Attempt</c> diagnostic header for debugging cross-origin cookie issues.
        /// </summary>
        /// <param name="response">The HTTP response to append the cookie to.</param>
        /// <param name="token">The refresh token value.</param>
        /// <param name="expiresUtc">Absolute expiry time in UTC.</param>
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

        /// <summary>Deletes the refresh-token cookie from the response (sign-out or token rotation).</summary>
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

        /// <summary>Reads the refresh-token cookie value from the incoming request, or <c>null</c> if absent.</summary>
        public string? ReadRefreshCookie(HttpRequest request)
        {
            return request.Cookies.TryGetValue(RefreshCookieName, out var v) ? v : null;
        }

        /* ───────────── Access-token cookie ───────────── */

        /// <summary>
        /// Appends the short-lived access-token cookie to the response.
        /// When <paramref name="expiresUtc"/> is <c>null</c>, the cookie becomes a session cookie
        /// (deleted when the browser closes).
        /// </summary>
        /// <param name="response">The HTTP response to append the cookie to.</param>
        /// <param name="token">The JWT access token value.</param>
        /// <param name="expiresUtc">Optional absolute expiry; null for session cookie.</param>
        public void SetAccessCookie(HttpResponse response, string token, DateTime? expiresUtc)
        {
            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            };

            // Only set Expires if provided → session cookie when null
            if (expiresUtc.HasValue)
            {
                opts.Expires = expiresUtc.Value;
            }

            response.Cookies.Append(AccessCookieName, token, opts);
        }

        /// <summary>Deletes the access-token cookie from the response (sign-out).</summary>
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

        /// <summary>Reads the access-token cookie value from the incoming request, or <c>null</c> if absent.</summary>
        public string? ReadAccessCookie(HttpRequest request)
        {
            return request.Cookies.TryGetValue(AccessCookieName, out var v) ? v : null;
        }
    }
}
