using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Platerly.Api.Services
{
    public class AuthCookieService
    {
        private readonly IConfiguration _config;
        public AuthCookieService(IConfiguration config) { _config = config; }

        public void SetRefreshCookie(HttpResponse response, string token, DateTime expiresUtc)
        {
            var cookieName = _config["AuthCookies:RefreshCookieName"] ?? "platerly.r";
            var domain = _config["AuthCookies:Domain"];
            var secure = bool.TryParse(_config["AuthCookies:Secure"], out var s) && s;
            var sameSite = (_config["AuthCookies:SameSite"] ?? "Lax").ToLowerInvariant();

            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = secure,
                IsEssential = true,
                Expires = expiresUtc,
                Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
                SameSite = sameSite switch
                {
                    "none" => SameSiteMode.None,
                    "strict" => SameSiteMode.Strict,
                    _ => SameSiteMode.Lax
                },
                Path = "/"
            };

            response.Cookies.Append(cookieName, token, opts);
        }

        public void ClearRefreshCookie(HttpResponse response)
        {
            var cookieName = _config["AuthCookies:RefreshCookieName"] ?? "platerly.r";
            var domain = _config["AuthCookies:Domain"];
            var opts = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                IsEssential = true,
                Expires = DateTime.UnixEpoch,
                Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
                SameSite = SameSiteMode.None,
                Path = "/"
            };
            response.Cookies.Append(cookieName, "", opts);
        }

        public string? ReadRefreshCookie(HttpRequest request)
        {
            var name = _config["AuthCookies:RefreshCookieName"] ?? "platerly.r";
            return request.Cookies.TryGetValue(name, out var v) ? v : null;
        }
    }
}
