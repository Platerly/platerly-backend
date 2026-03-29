using System.ComponentModel.DataAnnotations;

namespace Platerly.Api.Models
{
    /// <summary>
    /// Tracks the legal external sources that the Recipe Index Engine fetches from.
    /// Each source must have confirmed legal notes before activation.
    /// </summary>
    public class RecipeIndexSource
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(256)]
        public string SourceName { get; set; } = string.Empty;

        [Required, MaxLength(256)]
        public string SourceDomain { get; set; } = string.Empty;

        [Required, MaxLength(2048)]
        public string ApiEndpoint { get; set; } = string.Empty;

        public DateTime? LastIndexedAt { get; set; }

        /// <summary>
        /// Total recipes that passed the Halal Gate and were stored.
        /// </summary>
        public int TotalRecipesIndexed { get; set; } = 0;

        /// <summary>
        /// Total recipes rejected by the Halal Gate and discarded. Never stored.
        /// </summary>
        public int TotalRecipesDiscarded { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Whether this source requires an API key to access.
        /// </summary>
        public bool RequiresApiKey { get; set; } = false;

        /// <summary>
        /// Reference key name in appsettings / environment variables. Never the key value itself.
        /// </summary>
        [MaxLength(128)]
        public string? ApiKeyReference { get; set; }

        /// <summary>
        /// Legal basis for indexing this source — must be populated before activation.
        /// E.g. "Ingredient lists and cooking instructions are facts, not copyrightable."
        /// </summary>
        [Required, MaxLength(2048)]
        public string LegalNotes { get; set; } = string.Empty;

        /// <summary>
        /// Whether to honour robots.txt for this source.
        /// Always true unless source has explicitly granted crawl permission.
        /// </summary>
        public bool RespectRobotsTxt { get; set; } = true;

        /// <summary>
        /// Minimum milliseconds to wait between requests to this source.
        /// Default 1000ms (1 second) — be respectful to external APIs.
        /// </summary>
        public int RequestDelayMs { get; set; } = 1000;

        /// <summary>
        /// Whether thumbnails from this source may be displayed (linked, not re-hosted).
        /// Must be confirmed from the source's terms of service.
        /// </summary>
        public bool AllowsThumbnailDisplay { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public List<RecipeIndex> Recipes { get; set; } = new();
    }
}
