namespace Platerly.Api.Models.Sitemap
{
    public class SitemapUrl
    {
        public string Loc { get; set; } = default!;
        public DateTime LastModified { get; set; }
        public string ChangeFrequency { get; set; } = "daily";
        public double Priority { get; set; }
    }
}
