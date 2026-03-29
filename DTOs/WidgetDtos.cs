namespace Platerly.Api.DTOs
{
    public class WidgetConfigRequest
    {
        /// <summary>
        /// JSON branding object. Recognised keys:
        /// accentColor, backgroundColor, textColor, borderColor, placeholderText.
        /// Example: {"accentColor":"#22c55e","placeholderText":"Search ingredients..."}
        /// </summary>
        public string? ThemeJson { get; set; }
        public int MaxRecipes { get; set; } = 10;
        public string? DietaryFilters { get; set; }
        public string? RegionFilter { get; set; }
        public string? LogoUrl { get; set; }
    }

    public class WidgetConfigResponse
    {
        public Guid Id { get; set; }
        public string? ThemeJson { get; set; }
        public int MaxRecipes { get; set; }
        public string? DietaryFilters { get; set; }
        public string? RegionFilter { get; set; }
        public string? LogoUrl { get; set; }
        public bool IsActive { get; set; }

        /// <summary>The full script tag HTML to copy-paste into your site.</summary>
        public string ScriptTag { get; set; } = string.Empty;

        /// <summary>Direct URL of the embed.js loader script.</summary>
        public string EmbedUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Public branding config returned to embed.js — no auth required.
    /// </summary>
    public class WidgetPublicConfigDto
    {
        public string? LogoUrl { get; set; }
        public string AccentColor { get; set; } = "#22c55e";
        public string BackgroundColor { get; set; } = "#1a1a2e";
        public string TextColor { get; set; } = "#f1f5f9";
        public string BorderColor { get; set; } = "#334155";
        public string MutedColor { get; set; } = "#94a3b8";
        public string PlaceholderText { get; set; } = "Search for an ingredient...";
        public string Disclaimer { get; set; } =
            "Powered by Recipzee. Nickel data sourced from Rebelytics v9.1.1 — " +
            "257 peer-reviewed studies, 20,575 measurements. " +
            "For informational use only. Not a substitute for professional dietary advice.";
    }

    public class WidgetFeedRecipeDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string? Region { get; set; }
        public int PrepTimeMinutes { get; set; }
        public int CookTimeMinutes { get; set; }
        public string? Difficulty { get; set; }
        public List<string> Tags { get; set; } = new();
        public string AuthorUsername { get; set; } = string.Empty;
    }

    public class WidgetFeedResponse
    {
        public List<WidgetFeedRecipeDto> Recipes { get; set; } = new();
        public int TotalAvailable { get; set; }
    }
}
