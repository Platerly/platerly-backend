using System.Net;
using System.Text;

namespace Platerly.Api.Middleware
{
    /// <summary>
    /// Intercepts requests from search engine bots and social crawlers,
    /// forwards them to Prerender.io, and returns fully-rendered HTML.
    /// Regular users are unaffected.
    /// </summary>
    public class PrerenderMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string? _token;
        private readonly ILogger<PrerenderMiddleware> _logger;

        // Known bots that benefit from prerendering
        private static readonly HashSet<string> BotAgents = new(StringComparer.OrdinalIgnoreCase)
        {
            "googlebot", "bingbot", "yandex", "baiduspider", "facebookexternalhit",
            "twitterbot", "rogerbot", "linkedinbot", "embedly", "quora link preview",
            "showyoubot", "outbrain", "pinterest", "slackbot", "vkShare", "W3C_Validator",
            "whatsapp", "telegrambot", "applebot", "duckduckbot", "sogou", "exabot",
            "ia_archiver", "semrushbot", "ahrefsbot", "mj12bot", "dotbot"
        };

        // Only prerender HTML page requests, not API/asset calls
        private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".js", ".css", ".xml", ".less", ".png", ".jpg", ".jpeg", ".gif",
            ".pdf", ".doc", ".txt", ".ico", ".rss", ".zip", ".mp3", ".rar",
            ".exe", ".wmv", ".doc", ".avi", ".ppt", ".mpg", ".mpeg", ".tif",
            ".wav", ".mov", ".psd", ".ai", ".xls", ".mp4", ".m4a", ".swf",
            ".dat", ".dmg", ".iso", ".flv", ".m4v", ".torrent", ".woff",
            ".woff2", ".ttf", ".svg", ".webp", ".avif"
        };

        public PrerenderMiddleware(RequestDelegate next, IConfiguration config, ILogger<PrerenderMiddleware> logger)
        {
            _next = next;
            _token = config["Prerender:Token"];
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (ShouldPrerender(context))
            {
                await ServePrerendered(context);
                return;
            }

            await _next(context);
        }

        private bool ShouldPrerender(HttpContext context)
        {
            // Must have a token configured
            if (string.IsNullOrWhiteSpace(_token))
                return false;

            // Only GET requests
            if (!HttpMethods.IsGet(context.Request.Method))
                return false;

            // Skip API routes — only prerender frontend page routes
            var path = context.Request.Path.Value ?? "";
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
                return false;

            // Skip static file extensions
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext) && IgnoredExtensions.Contains(ext))
                return false;

            // Check user agent for known bots
            var userAgent = context.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(userAgent))
                return false;

            // Check if _escaped_fragment_ present (legacy AJAX crawling)
            if (context.Request.Query.ContainsKey("_escaped_fragment_"))
                return true;

            return BotAgents.Any(bot => userAgent.Contains(bot, StringComparison.OrdinalIgnoreCase));
        }

        private async Task ServePrerendered(HttpContext context)
        {
            var request = context.Request;
            var scheme = request.IsHttps ? "https" : "http";
            var originalUrl = $"{scheme}://{request.Host}{request.Path}{request.QueryString}";

            var prerenderUrl = $"https://service.prerender.io/{originalUrl}";

            _logger.LogInformation("Prerendering {Url} for bot: {UserAgent}",
                originalUrl, context.Request.Headers.UserAgent.ToString()[..Math.Min(80, context.Request.Headers.UserAgent.ToString().Length)]);

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.Add("X-Prerender-Token", _token);
                client.DefaultRequestHeaders.Add("User-Agent", "Recipzee-Prerender/1.0");

                var response = await client.GetAsync(prerenderUrl);
                var html = await response.Content.ReadAsStringAsync();

                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(html, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Prerender.io request failed for {Url}, falling through to normal pipeline", originalUrl);
                // Fall through gracefully — don't crash the app
                context.Response.Clear();
                await _next(context);
            }
        }
    }

    public static class PrerenderMiddlewareExtensions
    {
        public static IApplicationBuilder UsePrerender(this IApplicationBuilder app)
            => app.UseMiddleware<PrerenderMiddleware>();
    }
}
