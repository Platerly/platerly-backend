using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Services.Sitemap;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// XML Sitemap endpoint — layer: public, route: <c>sitemap.xml</c>.
    /// Generates and serves a standards-compliant XML sitemap (<c>sitemaps.org/schemas/sitemap/0.9</c>)
    /// for Recipzee's public-facing pages. The URL list is sourced from <see cref="ISitemapService"/>,
    /// which aggregates static routes and dynamic recipe/ingredient URLs from the database.
    /// The response is cached at the HTTP layer for 1 hour (<c>ResponseCache</c>) to avoid
    /// repeated database hits from search-engine crawlers. No authentication is required.
    /// </summary>
    [ApiController]
    [Route("sitemap.xml")]
    public class SitemapController : ControllerBase
    {
        private readonly ISitemapService _sitemapService;

        public SitemapController(ISitemapService sitemapService)
        {
            _sitemapService = sitemapService;
        }

        /// <summary>
        /// <c>GET sitemap.xml</c> — Generate and return the XML sitemap.
        /// Fetches all sitemap URLs from <see cref="ISitemapService.GetAllAsync"/>, then builds a
        /// well-formed XML document with <c>&lt;loc&gt;</c>, <c>&lt;lastmod&gt;</c>,
        /// <c>&lt;changefreq&gt;</c>, and <c>&lt;priority&gt;</c> elements for each URL.
        /// The response uses content type <c>application/xml; charset=utf-8</c> and is
        /// cached for 3 600 seconds (1 hour) via <see cref="ResponseCacheAttribute"/>.
        /// </summary>
        /// <returns>
        /// <c>200 OK</c> — UTF-8 encoded XML sitemap document conforming to the Sitemaps 0.9 schema.
        /// </returns>
        [HttpGet]
        [ResponseCache(Duration = 3600)] // 1 hour cache
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var urls = await _sitemapService.GetAllAsync(ct);

            // Use XNamespace to let LINQ to XML emit the xmlns attribute correctly,
            // rather than constructing the attribute string by hand.
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

            var urlset = new XElement(ns + "urlset",
                urls.Select(u =>
                    new XElement(ns + "url",
                        new XElement(ns + "loc", u.Loc),
                        // lastmod must be formatted as yyyy-MM-dd per the Sitemaps spec.
                        new XElement(ns + "lastmod", u.LastModified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XElement(ns + "changefreq", u.ChangeFrequency),
                        // Priority uses one decimal place: "0.8", not "0.800".
                        new XElement(ns + "priority", u.Priority.ToString("0.0", CultureInfo.InvariantCulture))
                    )
                )
            );

            var xml = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                urlset
            );

            return Content(xml.ToString(), "application/xml", Encoding.UTF8);
        }
    }
}
