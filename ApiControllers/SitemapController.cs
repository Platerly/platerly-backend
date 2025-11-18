using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Services.Sitemap;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Platerly.Api.ApiControllers
{
    [ApiController]
    [Route("sitemap.xml")]
    public class SitemapController : ControllerBase
    {
        private readonly ISitemapService _sitemapService;

        public SitemapController(ISitemapService sitemapService)
        {
            _sitemapService = sitemapService;
        }

        [HttpGet]
        [ResponseCache(Duration = 3600)] // 1 hour cache
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var urls = await _sitemapService.GetAllAsync(ct);

            // ✅ Use XNamespace instead of manual xmlns attribute
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

            var urlset = new XElement(ns + "urlset",
                urls.Select(u =>
                    new XElement(ns + "url",
                        new XElement(ns + "loc", u.Loc),
                        new XElement(ns + "lastmod", u.LastModified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XElement(ns + "changefreq", u.ChangeFrequency),
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
