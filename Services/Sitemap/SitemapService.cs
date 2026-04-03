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
            urls.Add(new SitemapUrl
            {
                Loc = $"{domain}/",
                LastModified = DateTime.UtcNow,
                Priority = 1.0,
                ChangeFrequency = "daily"
            });

            urls.Add(new SitemapUrl
            {
                Loc = $"{domain}/feed",
                LastModified = DateTime.UtcNow,
                Priority = 0.5,
                ChangeFrequency = "daily"
            });

            urls.Add(new SitemapUrl
            {
                Loc = $"{domain}/explore-chefs",
                LastModified = DateTime.UtcNow,
                Priority = 0.8,
                ChangeFrequency = "daily"
            });

            urls.Add(new SitemapUrl
            {
                Loc = $"{domain}/register",
                LastModified = DateTime.UtcNow,
                Priority = 0.6,
                ChangeFrequency = "monthly"
            });

            urls.Add(new SitemapUrl
            {
                Loc = $"{domain}/login",
                LastModified = DateTime.UtcNow,
                Priority = 0.3,
                ChangeFrequency = "monthly"
            });

            urls.Add(new SitemapUrl
            {
                Loc = $"{domain}/privacy",
                LastModified = DateTime.UtcNow,
                Priority = 0.2,
                ChangeFrequency = "yearly"
            });

            urls.Add(new SitemapUrl
            {
                Loc = $"{domain}/terms",
                LastModified = DateTime.UtcNow,
                Priority = 0.2,
                ChangeFrequency = "yearly"
            });

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
