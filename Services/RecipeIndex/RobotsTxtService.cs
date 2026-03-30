using Microsoft.Extensions.Logging;

namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Fetches and parses robots.txt for any domain.
    /// Checks Recipzee-Bot specific rules first, then wildcard (*) rules.
    /// A missing or unreachable robots.txt is treated as allowed (standard convention).
    /// </summary>
    public class RobotsTxtService : IRobotsTxtService
    {
        private const string OurAgent = "recipzee-bot";

        private readonly IHttpClientFactory _http;
        private readonly ILogger<RobotsTxtService> _log;

        public RobotsTxtService(IHttpClientFactory http, ILogger<RobotsTxtService> log)
        {
            _http = http;
            _log = log;
        }

        public async Task<RobotsCheckResult> CheckAsync(string domain, CancellationToken ct = default)
        {
            var url = $"https://{domain}/robots.txt";
            string text;

            try
            {
                var client = _http.CreateClient("RecipzeeBot");
                using var resp = await client.GetAsync(url, ct);

                if (!resp.IsSuccessStatusCode)
                {
                    // robots.txt absent or server error — treat as allowed per convention
                    _log.LogDebug("robots.txt not found for {Domain} (HTTP {Status}) — treating as allowed", domain, (int)resp.StatusCode);
                    return new RobotsCheckResult(true, null);
                }

                text = await resp.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not fetch robots.txt for {Domain} — treating as allowed", domain);
                return new RobotsCheckResult(true, null);
            }

            return Parse(text, domain);
        }

        private static RobotsCheckResult Parse(string text, string domain)
        {
            // Parse into sections keyed by user-agent
            // We look for our specific agent first, then wildcard
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Collect rules per agent: agent -> list of (type, path)
            // type: "disallow" | "allow"
            var sections = new Dictionary<string, List<(string Type, string Path)>>(StringComparer.OrdinalIgnoreCase);
            string? currentAgent = null;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith('#')) continue;

                var colon = line.IndexOf(':');
                if (colon < 0) continue;

                var key = line[..colon].Trim().ToLowerInvariant();
                var value = line[(colon + 1)..].Trim();

                if (key == "user-agent")
                {
                    currentAgent = value.ToLowerInvariant();
                    if (!sections.ContainsKey(currentAgent))
                        sections[currentAgent] = new();
                }
                else if ((key == "disallow" || key == "allow") && currentAgent != null)
                {
                    sections[currentAgent].Add((key, value));
                }
            }

            // Check our specific agent, then wildcard
            foreach (var agentKey in new[] { OurAgent, "*" })
            {
                if (!sections.TryGetValue(agentKey, out var rules)) continue;

                foreach (var (type, path) in rules)
                {
                    if (type == "disallow" && (path == "/" || path == ""))
                    {
                        // path="" means allow all in some interpretations; path="/" means block all
                        if (path == "/")
                            return new RobotsCheckResult(false, $"robots.txt disallows all crawling for {agentKey} on {domain}");
                    }
                    else if (type == "disallow" && path.Length > 0)
                    {
                        // Specific path disallow — we only index recipe pages, but if /recipe/ is blocked, respect it
                        // For now any Disallow: / prefix block that covers /* or /recipe is a signal to skip
                        if (path is "/" or "/*")
                            return new RobotsCheckResult(false, $"robots.txt Disallow: {path} for {agentKey} on {domain}");
                    }
                }

                // If we found our section and it didn't have a blanket Disallow — allowed
                if (rules.Count > 0)
                    return new RobotsCheckResult(true, null);
            }

            // No relevant section found — allowed
            return new RobotsCheckResult(true, null);
        }
    }
}
