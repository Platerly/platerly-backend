using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Platerly.Api.Data;
using Platerly.Api.Models.Sitemap;
using Platerly.Api.Services.Sitemap;

namespace Platerly.Api.Services.Sitemap
{
    /// <summary>
    /// Infrastructure service — generates the complete sitemap URL list for the Recipzee public domain.
    /// Depends on <see cref="PlaterlyDbContext"/> for dynamic content and <see cref="IConfiguration"/>
    /// for the public base URL (<c>PublicBaseUrl</c> or <c>AppSettings:PublicBaseUrl</c> config key,
    /// defaulting to <c>https://recipzee.com</c>).
    ///
    /// Produces four categories of URLs:
    ///   Static core pages (home, feed, explore, register, login, legal) with fixed priorities.
    ///   Dynamic recipe pages using SEO-friendly <c>/{username}/{slug}</c> paths (priority 0.8).
    ///   Dynamic user profile pages using <c>/profile/{username}</c> (priority 0.6).
    ///   Dynamic cookbook pages using <c>/cookbook/{id}</c> (priority 0.7).
    /// </summary>
    public class SitemapService : ISitemapService
    {
        private readonly PlaterlyDbContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<SitemapService> _log;

        public SitemapService(
            PlaterlyDbContext context,
            IConfiguration config,
            ILogger<SitemapService> log)
        {
            _context = context;
            _config = config;
            _log = log;
        }

        /// <summary>
        /// Builds the complete sitemap URL list by combining static core pages with
        /// database-driven recipe, user profile, and cookbook entries.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>An immutable list of <see cref="SitemapUrl"/> entries for XML serialisation.</returns>
        public async Task<IReadOnlyList<SitemapUrl>> GetAllAsync(CancellationToken ct)
        {
            var domain = (_config["PublicBaseUrl"] ?? _config["AppSettings:PublicBaseUrl"] ?? "https://recipzee.com").TrimEnd('/');

            var urls = new List<SitemapUrl>();

            // --- Static core pages ---
            var staticPages = new[]
            {
                // High-priority acquisition and discovery pages
                (Path: "/",                    Priority: 1.0, Freq: "daily"),
                (Path: "/explore-chefs",       Priority: 0.8, Freq: "daily"),
                (Path: "/explore-recipes",     Priority: 0.8, Freq: "daily"),
                (Path: "/explore-cookbooks",   Priority: 0.8, Freq: "daily"),
                (Path: "/trending",            Priority: 0.8, Freq: "daily"),
                (Path: "/ingredients",         Priority: 0.8, Freq: "weekly"),

                // B2B / developer pages — high SEO value
                (Path: "/api",                 Priority: 0.9, Freq: "weekly"),
                (Path: "/developer",           Priority: 0.8, Freq: "weekly"),
                (Path: "/roadmap",             Priority: 0.7, Freq: "monthly"),

                // Auth / onboarding
                (Path: "/register",            Priority: 0.6, Freq: "monthly"),
                (Path: "/login",               Priority: 0.3, Freq: "monthly"),

                // Feed (requires auth but still crawlable)
                (Path: "/feed",                Priority: 0.5, Freq: "daily"),

                // Support / informational
                (Path: "/contact",             Priority: 0.5, Freq: "monthly"),
                (Path: "/guidelines",          Priority: 0.4, Freq: "monthly"),
                (Path: "/research",            Priority: 0.4, Freq: "monthly"),

                // Legal / policy
                (Path: "/privacy",             Priority: 0.2, Freq: "yearly"),
                (Path: "/terms",               Priority: 0.2, Freq: "yearly"),
                (Path: "/refunds",             Priority: 0.2, Freq: "yearly"),
                (Path: "/cookie-policy",       Priority: 0.2, Freq: "yearly"),
                (Path: "/allergy-disclaimer",  Priority: 0.3, Freq: "yearly"),
            };

            foreach (var page in staticPages)
            {
                urls.Add(new SitemapUrl
                {
                    Loc = $"{domain}{page.Path}",
                    LastModified = DateTime.UtcNow,
                    Priority = page.Priority,
                    ChangeFrequency = page.Freq
                });
            }

            // --- Dynamic: Recipes (with SEO-friendly URLs) ---
            var recipes = await _context.Recipes
                .Include(r => r.User)
                .Where(r => !r.IsDeleted && r.Slug != null && r.User != null && r.User.Username != null)
                .Select(r => new { r.Slug, Username = r.User!.Username, r.CreatedAt, r.UpdatedAt })
                .ToListAsync(ct);

            foreach (var r in recipes)
            {
                urls.Add(new SitemapUrl
                {
                    Loc = $"{domain}/{r.Username!.ToLowerInvariant()}/{r.Slug}",
                    LastModified = r.UpdatedAt ?? r.CreatedAt,
                    Priority = 0.8,
                    ChangeFrequency = "weekly"
                });
            }

            // --- Dynamic: User profiles (using username for SEO-friendly URLs) ---
            var users = await _context.Users
                .Where(u => !string.IsNullOrEmpty(u.Username) && !u.Username.Contains("@"))
                .Select(u => new { u.Username, u.CreatedAt })
                .ToListAsync(ct);

            foreach (var u in users)
            {
                urls.Add(new SitemapUrl
                {
                    Loc = $"{domain}/profile/{u.Username!.ToLowerInvariant()}",
                    LastModified = u.CreatedAt,
                    Priority = 0.6,
                    ChangeFrequency = "weekly"
                });
            }

            // --- Dynamic: Cookbooks (with actual dates) ---
            var cookbooks = await _context.Cookbooks
                .Select(c => new { c.Id, c.CreatedAt, c.UpdatedAt })
                .ToListAsync(ct);

            foreach (var c in cookbooks)
            {
                urls.Add(new SitemapUrl
                {
                    Loc = $"{domain}/cookbook/{c.Id}",
                    LastModified = c.UpdatedAt ?? c.CreatedAt,
                    Priority = 0.7,
                    ChangeFrequency = "weekly"
                });
            }

            return urls;
        }
    }
}
