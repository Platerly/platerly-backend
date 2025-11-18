using Platerly.Api.Models.Sitemap;

namespace Platerly.Api.Services.Sitemap
{
    public interface ISitemapService
    {
        Task<IReadOnlyList<SitemapUrl>> GetAllAsync(CancellationToken ct);
        
    }
}
