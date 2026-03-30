using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platerly.Api.Data;
using RecipeIndexRecord = Platerly.Api.Models.RecipeIndex;

namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Crawls food blogs and recipe websites to build the halal recipe index.
    ///
    /// Discovery order per domain:
    ///   1. sitemap_index.xml → enumerate child sitemaps
    ///   2. sitemap.xml        → enumerate URLs directly
    ///   3. robots.txt Sitemap: directive
    ///   (No homepage crawl fallback — too broad, too slow)
    ///
    /// Per page:
    ///   1. JSON-LD @type:Recipe (preferred — clean, SEO-driven structured data)
    ///   2. HTML pattern fallback (WP Recipe Maker, Tasty Recipes, Recipe Card Blocks)
    ///
    /// Every extracted recipe is run through HalalGateService.
    /// Passing recipes are stored in RecipeIndex with IsAnalysed=false.
    /// The analysis pipeline picks them up automatically.
    /// </summary>
    public class GeneralRecipeIndexingService : IGeneralRecipeIndexingService
    {
        private const int RequestDelayMs = 2000;
        private const int MaxUrlsPerDomain = 500;   // safety cap per run
        private const int MaxRecipesPerDomain = 200; // halal-pass cap per run

        private readonly PlaterlyDbContext _db;
        private readonly IRobotsTxtService _robots;
        private readonly IHalalGateService _halal;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<GeneralRecipeIndexingService> _log;

        public GeneralRecipeIndexingService(
            PlaterlyDbContext db,
            IRobotsTxtService robots,
            IHalalGateService halal,
            IHttpClientFactory http,
            ILogger<GeneralRecipeIndexingService> log)
        {
            _db = db;
            _robots = robots;
            _halal = halal;
            _http = http;
            _log = log;
        }

        // ── Public entry points ─────────────────────────────────────────────

        public async Task<IReadOnlyList<GeneralIndexingStats>> RunAllAsync(CancellationToken ct = default)
        {
            var sources = await _db.RecipeIndexSources
                .Where(s => s.IsActive && s.RobotsAllowed != false)
                .OrderBy(s => s.LastIndexedAt ?? DateTime.MinValue)
                .ToListAsync(ct);

            var results = new List<GeneralIndexingStats>();
            foreach (var source in sources)
            {
                if (ct.IsCancellationRequested) break;
                var stats = await RunForSourceAsync(source, ct);
                results.Add(stats);
            }
            return results;
        }

        public async Task<GeneralIndexingStats> RunForDomainAsync(string domain, CancellationToken ct = default)
        {
            var source = await _db.RecipeIndexSources
                .FirstOrDefaultAsync(s => s.SourceDomain == domain, ct);

            if (source == null)
            {
                _log.LogWarning("RunForDomain: source not found for {Domain}", domain);
                return new GeneralIndexingStats(domain, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
            }

            return await RunForSourceAsync(source, ct);
        }

        // ── Core per-source logic ───────────────────────────────────────────

        private async Task<GeneralIndexingStats> RunForSourceAsync(
            Models.RecipeIndexSource source,
            CancellationToken ct)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var domain = source.SourceDomain;

            _log.LogInformation("[Indexer] Starting {Domain}", domain);

            // 1. Check blocked domains
            var blocked = await _db.BlockedDomains
                .AnyAsync(b => b.Domain == domain, ct);
            if (blocked)
            {
                _log.LogInformation("[Indexer] {Domain} is in BlockedDomains — skipping", domain);
                source.IsActive = false;
                await _db.SaveChangesAsync(ct);
                return new GeneralIndexingStats(domain, 0, 0, 0, 0, 0, 0, sw.Elapsed);
            }

            // 2. Check/refresh robots.txt
            if (source.RobotsCheckedAt == null || source.RobotsCheckedAt < DateTime.UtcNow.AddDays(-7))
            {
                var check = await _robots.CheckAsync(domain, ct);
                source.RobotsCheckedAt = DateTime.UtcNow;
                source.RobotsAllowed = check.Allowed;
                source.RobotsDisallowNote = check.DisallowNote;
                await _db.SaveChangesAsync(ct);

                if (!check.Allowed)
                {
                    _log.LogInformation("[Indexer] {Domain} robots.txt disallows crawling: {Note}", domain, check.DisallowNote);
                    source.IsActive = false;
                    await _db.SaveChangesAsync(ct);
                    return new GeneralIndexingStats(domain, 0, 0, 0, 0, 0, 0, sw.Elapsed);
                }
            }

            // 3. Discover recipe URLs via sitemap
            var urls = await DiscoverUrlsAsync(domain, source.BaseUrl, ct);
            _log.LogInformation("[Indexer] {Domain} — discovered {Count} URLs", domain, urls.Count);

            // 4. Load already-indexed URLs for this domain to skip dupes
            var existingUrls = await _db.RecipeIndex
                .Where(r => r.SourceDomain == domain)
                .Select(r => r.SourceUrl)
                .ToHashSetAsync(ct);

            int recipesFound = 0, passed = 0, failed = 0, alreadyIndexed = 0, errors = 0;

            foreach (var url in urls.Take(MaxUrlsPerDomain))
            {
                if (ct.IsCancellationRequested) break;
                if (passed >= MaxRecipesPerDomain) break;

                if (existingUrls.Contains(url))
                {
                    alreadyIndexed++;
                    continue;
                }

                await Task.Delay(RequestDelayMs, ct);

                RecipeExtract? extract = null;
                try
                {
                    extract = await FetchAndParseAsync(url, ct);
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "[Indexer] Error fetching {Url}", url);
                    errors++;
                    continue;
                }

                if (extract == null) continue;
                recipesFound++;

                // 5. Halal gate
                var ingredients = extract.Ingredients;
                var gateResult = _halal.Evaluate(ingredients);

                if (!gateResult.Passed)
                {
                    var trigger = gateResult.FailedIngredients.FirstOrDefault()?.IngredientName ?? "unknown";
                    _log.LogDebug("[Indexer] {Domain} — HALAL FAIL: {Title} (trigger: {Ingredient})",
                        domain, extract.Title, trigger);
                    failed++;
                    source.TotalRecipesDiscarded++;
                    continue;
                }

                // 6. Store
                try
                {
                    _db.RecipeIndex.Add(new RecipeIndexRecord
                    {
                        Title          = Truncate(extract.Title, 512),
                        SourceUrl      = url,
                        SourceDomain   = domain,
                        IngredientsJson = JsonSerializer.Serialize(ingredients),
                        InstructionsJson = "[]",
                        PrepTimeMinutes = extract.PrepTimeMinutes,
                        CookTimeMinutes = extract.CookTimeMinutes,
                        CuisineType    = Truncate(extract.CuisineType, 128),
                        ThumbnailUrl   = Truncate(extract.ThumbnailUrl, 2048),
                        SubmissionSource = "WebIndexed",
                        IsAnalysed     = false,
                        SourceId       = source.Id,
                    });

                    await _db.SaveChangesAsync(ct);
                    existingUrls.Add(url);
                    passed++;
                    source.TotalRecipesIndexed++;
                }
                catch (Exception ex) when (ex.Message.Contains("UNIQUE") || ex.Message.Contains("unique"))
                {
                    alreadyIndexed++;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "[Indexer] DB error saving recipe from {Url}", url);
                    errors++;
                }
            }

            source.LastIndexedAt = DateTime.UtcNow;
            source.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _log.LogInformation(
                "[Indexer] {Domain} complete — found:{Recipes} passed:{Passed} failed:{Failed} skipped:{Skip} errors:{Errors} in {Elapsed:F1}s",
                domain, recipesFound, passed, failed, alreadyIndexed, errors, sw.Elapsed.TotalSeconds);

            return new GeneralIndexingStats(domain, urls.Count, recipesFound, passed, failed, alreadyIndexed, errors, sw.Elapsed);
        }

        // ── URL Discovery (sitemap-based) ───────────────────────────────────

        private async Task<List<string>> DiscoverUrlsAsync(string domain, string? baseUrl, CancellationToken ct)
        {
            var client = _http.CreateClient("RecipzeeBot");
            var urls = new List<string>();

            // Try to get sitemap location from robots.txt first
            var robotsSitemapUrl = await TryGetSitemapFromRobots(client, domain, ct);

            var sitemapCandidates = new List<string>();
            if (robotsSitemapUrl != null) sitemapCandidates.Add(robotsSitemapUrl);
            sitemapCandidates.Add($"https://{domain}/sitemap_index.xml");
            sitemapCandidates.Add($"https://{domain}/sitemap.xml");
            if (baseUrl != null) sitemapCandidates.Add(baseUrl);

            foreach (var sitemapUrl in sitemapCandidates)
            {
                if (urls.Count >= MaxUrlsPerDomain) break;
                try
                {
                    var fetched = await FetchSitemapUrlsAsync(client, sitemapUrl, domain, ct);
                    urls.AddRange(fetched);
                    if (urls.Count > 0) break; // stop at first successful sitemap
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "[Indexer] Failed to fetch sitemap {Url}", sitemapUrl);
                }
            }

            // Deduplicate and filter to plausible recipe URLs
            return urls
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(u => IsLikelyRecipeUrl(u))
                .Take(MaxUrlsPerDomain)
                .ToList();
        }

        private async Task<string?> TryGetSitemapFromRobots(HttpClient client, string domain, CancellationToken ct)
        {
            try
            {
                var resp = await client.GetStringAsync($"https://{domain}/robots.txt", ct);
                foreach (var line in resp.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Sitemap:", StringComparison.OrdinalIgnoreCase))
                        return trimmed[8..].Trim();
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private async Task<List<string>> FetchSitemapUrlsAsync(
            HttpClient client, string sitemapUrl, string domain, CancellationToken ct)
        {
            var urls = new List<string>();

            using var resp = await client.GetAsync(sitemapUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return urls;

            var xml = await resp.Content.ReadAsStringAsync(ct);
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch { return urls; }

            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            // Sitemap index — recurse one level
            var sitemapRefs = doc.Descendants(ns + "sitemap")
                .Select(s => s.Element(ns + "loc")?.Value)
                .Where(u => u != null && u.Contains(domain))
                .ToList();

            if (sitemapRefs.Any())
            {
                foreach (var childUrl in sitemapRefs.Take(20)) // cap child sitemaps
                {
                    if (ct.IsCancellationRequested) break;
                    if (urls.Count >= MaxUrlsPerDomain) break;
                    await Task.Delay(500, ct);
                    try
                    {
                        var childUrls = await FetchSitemapUrlsAsync(client, childUrl!, domain, ct);
                        urls.AddRange(childUrls);
                    }
                    catch { /* continue */ }
                }
                return urls;
            }

            // Regular sitemap — collect <loc> entries
            urls.AddRange(
                doc.Descendants(ns + "url")
                   .Select(u => u.Element(ns + "loc")?.Value)
                   .Where(u => !string.IsNullOrEmpty(u) && u.Contains(domain))!
            );

            return urls;
        }

        private static bool IsLikelyRecipeUrl(string url)
        {
            // URLs that likely lead to individual recipe pages
            var lower = url.ToLowerInvariant();
            // Common recipe path patterns
            if (Regex.IsMatch(lower, @"/(recipe|recipes|dish|food|meal|cook)/")) return true;
            // Exclude pagination, categories, tags, authors, search
            if (Regex.IsMatch(lower, @"/(page|category|tag|author|search|wp-content|wp-admin|wp-json|feed|sitemap|cdn)")) return false;
            // If URL has a long slug (likely a post/recipe) vs short category-like path
            var path = new Uri(url).AbsolutePath.Trim('/');
            var segments = path.Split('/');
            // Single deep slug (e.g. /chicken-tikka-masala/) — likely a post
            if (segments.Length == 1 && path.Length > 10 && path.Contains('-')) return true;
            // Two segments where second looks like a slug
            if (segments.Length == 2 && segments[1].Length > 10 && segments[1].Contains('-')) return true;

            return false;
        }

        // ── Page Parsing ────────────────────────────────────────────────────

        private async Task<RecipeExtract?> FetchAndParseAsync(string url, CancellationToken ct)
        {
            var client = _http.CreateClient("RecipzeeBot");
            string html;
            try
            {
                using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode) return null;

                // Skip large non-HTML responses
                var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
                if (!contentType.Contains("html")) return null;

                var contentLength = resp.Content.Headers.ContentLength;
                if (contentLength > 2_000_000) return null; // skip pages > 2MB

                html = await resp.Content.ReadAsStringAsync(ct);
            }
            catch { return null; }

            // Try JSON-LD first
            var extract = TryParseJsonLd(html, url);
            if (extract != null) return extract;

            // Fall back to HTML patterns
            return TryParseHtmlPatterns(html, url);
        }

        // ── JSON-LD Parser ──────────────────────────────────────────────────

        private RecipeExtract? TryParseJsonLd(string html, string url)
        {
            // Find all <script type="application/ld+json"> blocks
            var matches = Regex.Matches(
                html,
                @"<script[^>]+type=['""]application/ld\+json['""][^>]*>([\s\S]*?)</script>",
                RegexOptions.IgnoreCase);

            foreach (Match m in matches)
            {
                var json = m.Groups[1].Value.Trim();
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Handle both single object and @graph array
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                        {
                            var r = TryExtractFromJsonLdNode(item, url);
                            if (r != null) return r;
                        }
                    }
                    else
                    {
                        // Check for @graph
                        if (root.TryGetProperty("@graph", out var graph))
                        {
                            foreach (var item in graph.EnumerateArray())
                            {
                                var r = TryExtractFromJsonLdNode(item, url);
                                if (r != null) return r;
                            }
                        }
                        else
                        {
                            var r = TryExtractFromJsonLdNode(root, url);
                            if (r != null) return r;
                        }
                    }
                }
                catch { /* malformed JSON — skip */ }
            }

            return null;
        }

        private static RecipeExtract? TryExtractFromJsonLdNode(JsonElement node, string url)
        {
            if (node.ValueKind != JsonValueKind.Object) return null;

            // Check @type == "Recipe" (may be array like ["Recipe","Thing"])
            if (!node.TryGetProperty("@type", out var typeEl)) return null;
            var typeStr = typeEl.ValueKind == JsonValueKind.Array
                ? string.Join(" ", typeEl.EnumerateArray().Select(t => t.GetString() ?? ""))
                : typeEl.GetString() ?? "";

            if (!typeStr.Contains("Recipe", StringComparison.OrdinalIgnoreCase)) return null;

            // Title
            var title = node.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) return null;

            // Ingredients
            var ingredients = new List<string>();
            if (node.TryGetProperty("recipeIngredient", out var ingEl) && ingEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var ing in ingEl.EnumerateArray())
                {
                    var s = ing.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) ingredients.Add(s.Trim());
                }
            }
            if (ingredients.Count == 0) return null;

            // Times (ISO 8601 duration like PT30M)
            int? prepTime = null, cookTime = null;
            if (node.TryGetProperty("prepTime", out var prepEl))
                prepTime = ParseIsoDuration(prepEl.GetString());
            if (node.TryGetProperty("cookTime", out var cookEl))
                cookTime = ParseIsoDuration(cookEl.GetString());

            // Cuisine
            string? cuisine = null;
            if (node.TryGetProperty("recipeCuisine", out var cuisineEl))
            {
                cuisine = cuisineEl.ValueKind == JsonValueKind.Array
                    ? cuisineEl.EnumerateArray().FirstOrDefault().GetString()
                    : cuisineEl.GetString();
            }

            // Thumbnail — prefer image from JSON-LD, fall back to og:image extracted separately
            string? thumbnail = null;
            if (node.TryGetProperty("image", out var imgEl))
            {
                thumbnail = imgEl.ValueKind == JsonValueKind.String
                    ? imgEl.GetString()
                    : imgEl.ValueKind == JsonValueKind.Object && imgEl.TryGetProperty("url", out var imgUrl)
                        ? imgUrl.GetString()
                        : imgEl.ValueKind == JsonValueKind.Array
                            ? imgEl.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.String
                                ? imgEl.EnumerateArray().First().GetString()
                                : imgEl.EnumerateArray().FirstOrDefault().TryGetProperty("url", out var aUrl)
                                    ? aUrl.GetString()
                                    : null
                            : null;
            }

            return new RecipeExtract(title, ingredients, prepTime, cookTime, cuisine, thumbnail);
        }

        private static int? ParseIsoDuration(string? iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            // PT1H30M → 90, PT45M → 45, P0D → 0
            var m = Regex.Match(iso, @"(?:(\d+)H)?(?:(\d+)M)?");
            if (!m.Success) return null;
            var hours = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
            var mins = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            return hours * 60 + mins;
        }

        // ── HTML Pattern Fallback ───────────────────────────────────────────

        private static RecipeExtract? TryParseHtmlPatterns(string html, string url)
        {
            var hdoc = new HtmlDocument();
            hdoc.LoadHtml(html);

            // Extract og:image first (used as thumbnail regardless of plugin)
            var ogImage = hdoc.DocumentNode
                .SelectSingleNode("//meta[@property='og:image']")
                ?.GetAttributeValue("content", null);

            string? title = null;
            var ingredients = new List<string>();

            // ── WP Recipe Maker ──────────────────────────────────────────
            var wprm = hdoc.DocumentNode.SelectSingleNode("//*[contains(@class,'wprm-recipe')]");
            if (wprm != null)
            {
                title = wprm.SelectSingleNode(".//*[contains(@class,'wprm-recipe-name')]")?.InnerText.Trim();
                ingredients = wprm.SelectNodes(".//*[contains(@class,'wprm-recipe-ingredient')]")
                    ?.Select(n => HtmlEntity.DeEntitize(n.InnerText).Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList() ?? new();
            }

            // ── Tasty Recipes ────────────────────────────────────────────
            if (ingredients.Count == 0)
            {
                var tasty = hdoc.DocumentNode.SelectSingleNode("//*[contains(@class,'tasty-recipes')]");
                if (tasty != null)
                {
                    title ??= tasty.SelectSingleNode(".//*[contains(@class,'tasty-recipes-title')]")?.InnerText.Trim();
                    ingredients = tasty.SelectNodes(".//*[contains(@class,'tasty-recipes-ingredients')]//li")
                        ?.Select(n => HtmlEntity.DeEntitize(n.InnerText).Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList() ?? new();
                }
            }

            // ── Recipe Card Blocks (Delicious Recipes / WPRM alternatives) ─
            if (ingredients.Count == 0)
            {
                var rcb = hdoc.DocumentNode.SelectSingleNode("//*[contains(@class,'recipe-card')]");
                if (rcb != null)
                {
                    title ??= rcb.SelectSingleNode(".//*[contains(@class,'recipe-card-title')]")?.InnerText.Trim();
                    ingredients = rcb.SelectNodes(".//ul[contains(@class,'ingredients')]//li")
                        ?.Select(n => HtmlEntity.DeEntitize(n.InnerText).Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList() ?? new();
                }
            }

            // ── Mediavine Create ─────────────────────────────────────────
            if (ingredients.Count == 0)
            {
                var mv = hdoc.DocumentNode.SelectSingleNode("//*[contains(@class,'mv-create-ing-list')]");
                if (mv != null)
                {
                    ingredients = mv.SelectNodes(".//li")
                        ?.Select(n => HtmlEntity.DeEntitize(n.InnerText).Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList() ?? new();
                    title ??= hdoc.DocumentNode
                        .SelectSingleNode("//*[contains(@class,'mv-create-title')]")?.InnerText.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(title) || ingredients.Count == 0) return null;

            // Fall back to page <title> if recipe name not found
            title ??= hdoc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(title)) return null;

            return new RecipeExtract(title, ingredients, null, null, null, ogImage);
        }

        private static string? Truncate(string? s, int max)
            => s == null ? null : s.Length <= max ? s : s[..max];

        // ── Internal record ─────────────────────────────────────────────────
        private record RecipeExtract(
            string Title,
            List<string> Ingredients,
            int? PrepTimeMinutes,
            int? CookTimeMinutes,
            string? CuisineType,
            string? ThumbnailUrl
        );
    }
}
