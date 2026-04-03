using Platerly.Api.Models.Sitemap;

namespace Platerly.Api.Services.Sitemap
{
    /// <summary>Provides all sitemap URLs for the dynamic XML sitemap endpoint.</summary>
    public interface ISitemapService
    {
        Task<IReadOnlyList<SitemapUrl>> GetAllAsync(CancellationToken ct);
        
    }
}
