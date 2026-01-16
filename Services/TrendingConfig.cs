namespace Platerly.Api.Services
{
    /// <summary>
    /// Configuration options for the TrendingRecomputeService.
    /// </summary>
    public class TrendingConfig
    {
        public const string SectionName = "Trending";

        /// <summary>
        /// Interval between trending recomputation runs in minutes. Default: 60
        /// </summary>
        public int IntervalMinutes { get; set; } = 60;

        /// <summary>
        /// Minimum startup jitter in seconds to avoid thundering herd. Default: 3
        /// </summary>
        public int StartupJitterMinSeconds { get; set; } = 3;

        /// <summary>
        /// Maximum startup jitter in seconds. Default: 12
        /// </summary>
        public int StartupJitterMaxSeconds { get; set; } = 12;

        /// <summary>
        /// How many days back to consider recipes as "recent" for trending. Default: 180
        /// </summary>
        public int CreatedWindowDays { get; set; } = 180;

        /// <summary>
        /// How many days back to consider interactions. Default: 30
        /// </summary>
        public int InteractionWindowDays { get; set; } = 30;

        /// <summary>
        /// Maximum number of recipes to cache in trending list. Default: 100
        /// </summary>
        public int CacheSize { get; set; } = 100;

        /// <summary>
        /// If true, seed fallback data when no candidates exist. Default: true
        /// </summary>
        public bool EnableNeverEmptyFallback { get; set; } = true;

        /// <summary>
        /// Rank threshold for sending "recipe is trending" notifications. Default: 10
        /// </summary>
        public int NotificationThreshold { get; set; } = 10;

        // Scoring weights
        public double LikeWeight { get; set; } = 3.0;
        public double RepostWeight { get; set; } = 4.0;
        public double RemixWeight { get; set; } = 6.0;
        public double CommentWeight { get; set; } = 2.0;
        public double ViewWeight { get; set; } = 0.1;
        public double SaveWeight { get; set; } = 5.0;
    }
}
