using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platerly.Api.Data;
using Platerly.Api.Services;
using static Platerly.Api.Services.AllergenResolver;

namespace Platerly.Api.Controllers
{
    /// <summary>
    /// Public controller that serves Open Graph / Twitter Card share pages for recipes and cookbooks.
    /// Each endpoint renders a minimal HTML page populated with social meta tags, then immediately
    /// performs a client-side redirect to the canonical SPA URL. Social crawlers (Facebook, Twitter,
    /// Slack, etc.) harvest the meta tags without following the redirect.
    /// Also serves <c>/og/recipe/{id}.png</c> and <c>/og/cookbook/{id}.png</c> image routes that
    /// redirect to the actual cover/recipe image (or a fallback).
    /// Base routes: <c>api/share/…</c> and <c>/og/…</c> (public root).
    /// All endpoints are anonymous (<c>[AllowAnonymous]</c> on the class).
    /// Responses are cached for 6 hours at the CDN/proxy level.
    /// </summary>
    [ApiController]
    [Route("api/[Controller]")]
    [AllowAnonymous]
    public class ShareController : ControllerBase
    {
        private readonly PlaterlyDbContext _db;
        private readonly IConfiguration _config;
        private readonly IAllergenResolver _allergenResolver;
        private readonly ILogger<ShareController> _logger;

        public ShareController(
            PlaterlyDbContext db,
            IConfiguration config,
            IAllergenResolver allergenResolver,
            ILogger<ShareController> logger)
        {
            _db = db;
            _config = config;
            _allergenResolver = allergenResolver;
            _logger = logger;
        }

        private static string Root(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s.EndsWith("ies")) return s[..^3] + "y";
            if (s.EndsWith("es")) return s[..^2];
            if (s.EndsWith("s")) return s[..^1];
            return s;
        }

        private static bool LooseMatch(string ingLower, string termLower)
        {
            if (string.IsNullOrWhiteSpace(ingLower) || string.IsNullOrWhiteSpace(termLower)) return false;
            if (ingLower.Contains(termLower) || termLower.Contains(ingLower)) return true;
            var rIng = Root(ingLower);
            var rTerm = Root(termLower);
            return rIng.Contains(rTerm) || rTerm.Contains(rIng);
        }

        /// <summary>
        /// HEAD alias for <see cref="ShareRecipe"/> so that client-side preflight pings do not
        /// receive a 405 Method Not Allowed.
        /// </summary>
        /// <param name="id">Recipe GUID.</param>
        // HEAD so the client-side HEAD ping doesn't 405
        [HttpHead("recipe/{id:guid}")]
        public Task<IActionResult> ShareRecipeHead([FromRoute] Guid id) => ShareRecipe(id);

        /// <summary>
        /// Generate and return the Open Graph / Twitter Card share page for a recipe.
        /// Includes OG title, description (with difficulty, time, tags, and top allergen warnings),
        /// image, and a canonical URL. Contains a <c>meta http-equiv="refresh"</c> redirect so
        /// regular browsers are forwarded to the SPA immediately.
        /// Nickel allergen severity is computed live using <see cref="IAllergenResolver"/>.
        /// Response is cached for 6 hours.
        /// </summary>
        /// <param name="id">The recipe GUID.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>text/html</c> share page.<br/>
        /// <c>404</c> — recipe not found.
        /// </returns>
        [HttpGet("recipe/{id:guid}")]
        [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> ShareRecipe([FromRoute] Guid id)
        {
            try
            {
                var r = await _db.Recipes
                    .Where(x => x.Id == id)
                    .Select(x => new
                    {
                        x.Id,
                        x.Title,
                        x.Description,
                        x.ImageUrl,
                        x.Difficulty,
                        PrepTimeMinutes = x.PrepTimeMinutes,
                        CookTimeMinutes = x.CookTimeMinutes,
                        TagNames = x.RecipeTags.Select(rt => rt.Tag.Name).ToList(),
                        IngredientNames = x.Ingredients
                            .Select(i => i.GlobalIngredient != null ? i.GlobalIngredient.Name : "")
                            .ToList()
                    })
                    .FirstOrDefaultAsync();

                if (r == null)
                {
                    _logger.LogWarning("Recipe not found for ID: {Id}", id);
                    return NotFound("Recipe not found");
                }

                var ingNamesLower = r.IngredientNames
                    .Select(n => (n ?? string.Empty).Trim().ToLowerInvariant())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToList();

                var expandedWithSeverity = (_allergenResolver as AllergenResolver)?.ExpandWithSeverity(new[] { "nickel" })
                    ?? new List<(string term, Severity severity)>();

                var matchedSeverities = new Dictionary<string, string>();
                foreach (var ing in ingNamesLower)
                {
                    Severity? best = null;
                    foreach (var a in expandedWithSeverity)
                    {
                        var term = (a.term ?? "").ToLowerInvariant();
                        if (LooseMatch(ing, term))
                        {
                            if (best == null || a.severity > best) best = a.severity;
                        }
                    }
                    if (best != null && best.Value >= Severity.Medium)
                        matchedSeverities[ing] = best.Value.ToString().ToLowerInvariant();
                }

                var allergenList = matchedSeverities.Keys.Distinct().Take(3).ToList();

                // Public web origin (NOT the API hostname)
                var publicBase = _config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://www.recipzee.com";
                var site = "Recipzee";
                var canonicalUrl = $"{publicBase}/recipe/{r.Id}";

                // Optional cache-buster passthrough (?v=...)
                var vParam = HttpContext?.Request?.Query["v"].FirstOrDefault();
                var cacheBust = string.IsNullOrWhiteSpace(vParam) ? "1" : vParam.Trim();

                // We keep the share image on the public domain.
                // Crawlers will request /og/recipe/{id}.png which you can later upgrade to a rendered 1200x630 image.
                var imageBase = $"{publicBase}/og/recipe/{r.Id}.png";
                var shareImage = $"{publicBase}/api/share/og/recipe/{r.Id}.png?v={WebUtility.UrlEncode(cacheBust)}";

                string? timePart = (r.PrepTimeMinutes > 0 ? $"{r.PrepTimeMinutes} min prep" : null) is string p &&
                                   (r.CookTimeMinutes > 0 ? $"{r.CookTimeMinutes} min cook" : null) is string c
                    ? $"{p}, {c}"
                    : (r.PrepTimeMinutes > 0 ? $"{r.PrepTimeMinutes} min prep" : (r.CookTimeMinutes > 0 ? $"{r.CookTimeMinutes} min cook" : null));

                var tagList = (r.TagNames ?? new List<string>())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .Select(t => $"#{t}");

                string desc = string.Join(" · ", new[]
                {
                    r.Difficulty.HasValue ? $"Difficulty: {r.Difficulty.Value}" : null,
                    timePart,
                    tagList.Any() ? string.Join(" ", tagList) : null,
                    allergenList.Any() ? $"⚠️ Allergens: {string.Join(", ", allergenList)}" : null
                }.Where(s => !string.IsNullOrWhiteSpace(s)));

                if (desc.Length > 240) desc = desc[..240] + "…";

                const int OG_W = 1200;
                const int OG_H = 630;

                string html = $@"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<title>{E($"{r.Title} — {site}")}</title>
<link rel=""canonical"" href=""{canonicalUrl}"" />

<meta property=""og:type"" content=""article"">
<meta property=""og:site_name"" content=""{site}"">
<meta property=""og:title"" content=""{E(r.Title)}"">
<meta property=""og:description"" content=""{E(desc)}"">
<meta property=""og:url"" content=""{canonicalUrl}"">
<meta property=""og:image"" content=""{shareImage}"">
<meta property=""og:image:secure_url"" content=""{shareImage}"">
<meta property=""og:image:width"" content=""{OG_W}"">
<meta property=""og:image:height"" content=""{OG_H}"">
<meta property=""og:image:alt"" content=""{E($"{r.Title} on {site}")}"">

<meta name=""twitter:card"" content=""summary_large_image"">
<meta name=""twitter:title"" content=""{E(r.Title)}"">
<meta name=""twitter:description"" content=""{E(desc)}"">
<meta name=""twitter:image"" content=""{shareImage}"">
<meta name=""twitter:url"" content=""{canonicalUrl}"">
<meta name=""twitter:domain"" content=""recipzee.com"">
<meta name=""twitter:site"" content=""@recipzee"">

<meta http-equiv=""refresh"" content=""0; url={canonicalUrl}"">
<style>
  html,body{{background:#081615;color:#cbe3df;font-family:system-ui,-apple-system,Segoe UI,Roboto,Ubuntu,Arial,sans-serif}}
  .wrap{{display:flex;min-height:100vh;align-items:center;justify-content:center;text-align:center;padding:24px}}
  a{{color:#5EEAD4}}
</style>
</head>
<body>
  <div class=""wrap"">
    <div>
      <h1>{E(r.Title)}</h1>
      <p>Opening recipe… If you are not redirected, <a href=""{canonicalUrl}"">tap here</a>.</p>
    </div>
  </div>
</body>
</html>";

                return Content(html, "text/html; charset=utf-8");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing share request for recipe ID: {Id}", id);
                return NotFound("An error occurred while processing the share request.");
            }
        }

        /// <summary>
        /// Serve the OG image for a recipe from the public root path
        /// (<c>/og/recipe/{id}.png</c>). Redirects to the recipe's stored image URL,
        /// or to the site-wide fallback image if none is set.
        /// Response is cached for 6 hours.
        /// </summary>
        /// <param name="id">The recipe GUID.</param>
        /// <returns><c>302 Redirect</c> to the image URL.</returns>
        // Public path (root) — crawlers will request https://www.recipzee.com/og/recipe/{id}.png
        [HttpGet("/og/recipe/{id:guid}.png")]
        [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> OgRecipePngRoot([FromRoute] Guid id)
        {
            var imageUrl = await _db.Recipes
                .Where(r => r.Id == id)
                .Select(r => r.ImageUrl)
                .FirstOrDefaultAsync();

            var publicBase = _config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://www.recipzee.com";
            var fallback = $"{publicBase}/og-preview.png";
            return Redirect(string.IsNullOrWhiteSpace(imageUrl) ? fallback : imageUrl);
        }

        /// <summary>
        /// API-scoped alias for <see cref="OgRecipePngRoot"/>. Allows the Azure Static Web App
        /// rewrite rule to map <c>/og/recipe/*</c> → <c>/api/share/og/recipe/*</c>.
        /// Behaviour is identical to the root variant.
        /// </summary>
        /// <param name="id">The recipe GUID.</param>
        /// <returns><c>302 Redirect</c> to the image URL.</returns>
        // API-scoped alias — lets SWA rewrite /og/recipe/* → /api/share/og/recipe/*
        [HttpGet("og/recipe/{id:guid}.png")]
        [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> OgRecipePngApi([FromRoute] Guid id)
        {
            var imageUrl = await _db.Recipes
                .Where(r => r.Id == id)
                .Select(r => r.ImageUrl)
                .FirstOrDefaultAsync();

            var publicBase = _config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://www.recipzee.com";
            var fallback = $"{publicBase}/og-preview.png";
            return Redirect(string.IsNullOrWhiteSpace(imageUrl) ? fallback : imageUrl);
        }

        // ==================== COOKBOOK SHARING ====================

        /// <summary>
        /// HEAD alias for <see cref="ShareCookbook"/>. Prevents 405 responses on preflight pings.
        /// </summary>
        /// <param name="id">Cookbook GUID.</param>
        // HEAD so the client-side HEAD ping doesn't 405
        [HttpHead("cookbook/{id:guid}")]
        public Task<IActionResult> ShareCookbookHead([FromRoute] Guid id) => ShareCookbook(id);

        /// <summary>
        /// Generate and return the Open Graph / Twitter Card share page for a public cookbook.
        /// Only cookbooks with <c>IsPublic = true</c> are accessible; private cookbooks return 404.
        /// Description includes recipe count and owner username. Response is cached for 6 hours.
        /// </summary>
        /// <param name="id">The cookbook GUID.</param>
        /// <returns>
        /// <c>200 OK</c> — <c>text/html</c> share page.<br/>
        /// <c>404</c> — cookbook not found or is private.
        /// </returns>
        [HttpGet("cookbook/{id:guid}")]
        [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> ShareCookbook([FromRoute] Guid id)
        {
            try
            {
                var c = await _db.Cookbooks
                    .Where(x => x.Id == id && x.IsPublic)
                    .Select(x => new
                    {
                        x.Id,
                        x.Title,
                        x.Description,
                        x.CoverImageUrl,
                        RecipeCount = x.Recipes.Count,
                        OwnerUsername = x.User != null ? x.User.Username : null
                    })
                    .FirstOrDefaultAsync();

                if (c == null)
                {
                    _logger.LogWarning("Cookbook not found or not public for ID: {Id}", id);
                    return NotFound("Cookbook not found or is private");
                }

                // Public web origin (NOT the API hostname)
                var publicBase = _config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://www.recipzee.com";
                var site = "Recipzee";
                var canonicalUrl = $"{publicBase}/cookbook/{c.Id}";

                // Optional cache-buster passthrough (?v=...)
                var vParam = HttpContext?.Request?.Query["v"].FirstOrDefault();
                var cacheBust = string.IsNullOrWhiteSpace(vParam) ? "1" : vParam.Trim();

                // Share image - use cover image or fallback
                var shareImage = !string.IsNullOrWhiteSpace(c.CoverImageUrl)
                    ? c.CoverImageUrl
                    : $"{publicBase}/og-preview.png";

                // Build description
                var descParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(c.Description))
                {
                    var desc = c.Description.Length > 150 ? c.Description[..150] + "…" : c.Description;
                    descParts.Add(desc);
                }
                descParts.Add($"{c.RecipeCount} recipe{(c.RecipeCount == 1 ? "" : "s")}");
                if (!string.IsNullOrWhiteSpace(c.OwnerUsername))
                {
                    descParts.Add($"by @{c.OwnerUsername}");
                }

                string desc2 = string.Join(" · ", descParts);
                if (desc2.Length > 240) desc2 = desc2[..240] + "…";

                const int OG_W = 1200;
                const int OG_H = 630;

                string html = $@"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<title>{E($"{c.Title} — {site}")}</title>
<link rel=""canonical"" href=""{canonicalUrl}"" />

<meta property=""og:type"" content=""article"">
<meta property=""og:site_name"" content=""{site}"">
<meta property=""og:title"" content=""{E(c.Title)}"">
<meta property=""og:description"" content=""{E(desc2)}"">
<meta property=""og:url"" content=""{canonicalUrl}"">
<meta property=""og:image"" content=""{shareImage}"">
<meta property=""og:image:secure_url"" content=""{shareImage}"">
<meta property=""og:image:width"" content=""{OG_W}"">
<meta property=""og:image:height"" content=""{OG_H}"">
<meta property=""og:image:alt"" content=""{E($"{c.Title} on {site}")}"">

<meta name=""twitter:card"" content=""summary_large_image"">
<meta name=""twitter:title"" content=""{E(c.Title)}"">
<meta name=""twitter:description"" content=""{E(desc2)}"">
<meta name=""twitter:image"" content=""{shareImage}"">
<meta name=""twitter:url"" content=""{canonicalUrl}"">
<meta name=""twitter:domain"" content=""recipzee.com"">
<meta name=""twitter:site"" content=""@recipzee"">

<meta http-equiv=""refresh"" content=""0; url={canonicalUrl}"">
<style>
  html,body{{background:#081615;color:#cbe3df;font-family:system-ui,-apple-system,Segoe UI,Roboto,Ubuntu,Arial,sans-serif}}
  .wrap{{display:flex;min-height:100vh;align-items:center;justify-content:center;text-align:center;padding:24px}}
  a{{color:#5EEAD4}}
</style>
</head>
<body>
  <div class=""wrap"">
    <div>
      <h1>{E(c.Title)}</h1>
      <p>Opening cookbook… If you are not redirected, <a href=""{canonicalUrl}"">tap here</a>.</p>
    </div>
  </div>
</body>
</html>";

                return Content(html, "text/html; charset=utf-8");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing share request for cookbook ID: {Id}", id);
                return NotFound("An error occurred while processing the share request.");
            }
        }

        /// <summary>
        /// Serve the OG image for a public cookbook from the root path
        /// (<c>/og/cookbook/{id}.png</c>). Redirects to the cookbook's cover image URL,
        /// or the site-wide fallback if none is set or the cookbook is private.
        /// </summary>
        /// <param name="id">The cookbook GUID.</param>
        /// <returns><c>302 Redirect</c> to the image URL.</returns>
        // Public path (root) — crawlers will request https://www.recipzee.com/og/cookbook/{id}.png
        [HttpGet("/og/cookbook/{id:guid}.png")]
        [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> OgCookbookPngRoot([FromRoute] Guid id)
        {
            var imageUrl = await _db.Cookbooks
                .Where(c => c.Id == id && c.IsPublic)
                .Select(c => c.CoverImageUrl)
                .FirstOrDefaultAsync();

            var publicBase = _config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://www.recipzee.com";
            var fallback = $"{publicBase}/og-preview.png";
            return Redirect(string.IsNullOrWhiteSpace(imageUrl) ? fallback : imageUrl);
        }

        /// <summary>
        /// API-scoped alias for <see cref="OgCookbookPngRoot"/>. Supports the Azure Static Web App
        /// rewrite rule mapping <c>/og/cookbook/*</c> → <c>/api/share/og/cookbook/*</c>.
        /// </summary>
        /// <param name="id">The cookbook GUID.</param>
        /// <returns><c>302 Redirect</c> to the image URL.</returns>
        // API-scoped alias — lets SWA rewrite /og/cookbook/* → /api/share/og/cookbook/*
        [HttpGet("og/cookbook/{id:guid}.png")]
        [ResponseCache(Duration = 21600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> OgCookbookPngApi([FromRoute] Guid id)
        {
            var imageUrl = await _db.Cookbooks
                .Where(c => c.Id == id && c.IsPublic)
                .Select(c => c.CoverImageUrl)
                .FirstOrDefaultAsync();

            var publicBase = _config["PublicBaseUrl"]?.TrimEnd('/') ?? "https://www.recipzee.com";
            var fallback = $"{publicBase}/og-preview.png";
            return Redirect(string.IsNullOrWhiteSpace(imageUrl) ? fallback : imageUrl);
        }

        private static string E(string s) => WebUtility.HtmlEncode(s);
    }
}
