namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Hosted background service that runs the recipe indexer on a 24-hour schedule.
    ///
    /// First run: executes immediately on startup (no delay) to populate the index.
    /// Subsequent runs: waits 24 hours between each pass.
    ///
    /// Uses IServiceScopeFactory to create a fresh DI scope per run because
    /// RecipeIndexingService depends on scoped services (PlaterlyDbContext).
    /// </summary>
    public class RecipeIndexingBackgroundService : BackgroundService
    {
        private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecipeIndexingBackgroundService> _logger;

        public RecipeIndexingBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<RecipeIndexingBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger       = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[RecipeIndexer] Background service started. " +
                "First run begins immediately; subsequent runs every 24 hours.");

            // Small startup delay to let the rest of the application initialise
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);

                _logger.LogInformation("[RecipeIndexer] Next run in {Hours} hours.", RunInterval.TotalHours);
                await Task.Delay(RunInterval, stoppingToken);
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            _logger.LogInformation("[RecipeIndexer] Starting indexing run at {Time:u}.", DateTime.UtcNow);

            try
            {
                using var scope   = _scopeFactory.CreateScope();
                var indexer       = scope.ServiceProvider.GetRequiredService<IRecipeIndexingService>();

                var allStats = await indexer.RunAsync(ct);

                foreach (var stats in allStats)
                {
                    _logger.LogInformation(
                        "[RecipeIndexer] Source={Source} | Fetched={Fetched} | Stored={Stored} | " +
                        "Discarded={Discarded} | Skipped={Skipped} | Time={Time}ms",
                        stats.Source,
                        stats.TotalFetched,
                        stats.TotalStored,
                        stats.TotalDiscarded,
                        stats.TotalSkipped,
                        stats.ElapsedMs);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[RecipeIndexer] Run cancelled (application shutting down).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RecipeIndexer] Unhandled error during indexing run. " +
                    "Will retry in {Hours} hours.", RunInterval.TotalHours);
            }
        }
    }
}
