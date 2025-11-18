using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Platerly.Api.Data;
using Platerly.Api.Models.Sitemap;
using Platerly.Api.Services.Sitemap;

namespace Platerly.Api.Services.Sitemap
{
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

        public async Task<IReadOnlyList<SitemapUrl>> GetAllAsync(CancellationToken ct)
        {
            var domain = _config["AppSettings:PublicBaseUrl"] ?? "https://platerly.com";

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
                Loc = $"{domain}/explore",
                LastModified = DateTime.UtcNow,
                Priority = 0.9,
                ChangeFrequency = "daily"
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
                Loc = $"{domain}/register",
                LastModified = DateTime.UtcNow,
                Priority = 0.4,
                ChangeFrequency = "monthly"
            });

            // --- Dynamic: Recipes ---
            var recipes = await _context.Recipes
                .Select(r => new { r.Id })
                .ToListAsync(ct);

            foreach (var r in recipes)
            {
                urls.Add(new SitemapUrl
                {
                    Loc = $"{domain}/recipe/{r.Id}",
                    LastModified = DateTime.UtcNow, // we'll improve this with Created/Updated later
                    Priority = 0.8,
                    ChangeFrequency = "daily"
                });
            }

            // --- Dynamic: User profiles ---
            var users = await _context.Users
                .Select(u => new { u.Id })
                .ToListAsync(ct);

            foreach (var u in users)
            {
                urls.Add(new SitemapUrl
                {
                    Loc = $"{domain}/chef/{u.Id}", // we can switch to /chef/{username} later
                    LastModified = DateTime.UtcNow,
                    Priority = 0.7,
                    ChangeFrequency = "daily"
                });
            }

            // --- Dynamic: Cookbooks ---
            var cookbooks = await _context.Cookbooks
                .Select(c => new { c.Id })
                .ToListAsync(ct);

            foreach (var c in cookbooks)
            {
                urls.Add(new SitemapUrl
                {
                    Loc = $"{domain}/cookbook/{c.Id}",
                    LastModified = DateTime.UtcNow,
                    Priority = 0.7,
                    ChangeFrequency = "daily"
                });
            }

            return urls;
        }
    }
}
