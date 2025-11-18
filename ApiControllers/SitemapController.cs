using Microsoft.AspNetCore.Mvc;
using Platerly.Api.Services;
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

            var urlset = new XElement("urlset",
                new XAttribute("xmlns", "http://www.sitemaps.org/schemas/sitemap/0.9"),
                urls.Select(u =>
                    new XElement("url",
                        new XElement("loc", u.Loc),
                        new XElement("lastmod", u.LastModified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XElement("changefreq", u.ChangeFrequency),
                        new XElement("priority", u.Priority.ToString("0.0", CultureInfo.InvariantCulture))
                    )
                )
            );

            var xml = new XDocument(urlset);
            return Content(xml.ToString(), "application/xml", Encoding.UTF8);
        }
    }
}
