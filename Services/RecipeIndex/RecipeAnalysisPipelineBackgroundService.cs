namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Hosted background service that runs the recipe analysis pipeline on a schedule.
    ///
    /// First run: executes 10 seconds after startup to allow the application to finish
    /// initialising before the first database query. The TheMealDB indexer also uses a
    /// 15-second startup delay, so the analysis pipeline starts effectively in parallel
    /// and will find new rows shortly after they are committed by the indexer.
    ///
    /// Subsequent runs: every 15 minutes, ensuring newly indexed recipes receive allergen
    /// and nickel analysis promptly without waiting for a full 24-hour indexer cycle.
    ///
    /// Uses <see cref="IServiceScopeFactory"/> to create a fresh DI scope per run because
    /// <c>RecipeAnalysisPipelineService</c> depends on scoped services (<c>PlaterlyDbContext</c>).
    /// </summary>
    public class RecipeAnalysisPipelineBackgroundService : BackgroundService
    {
        /// <summary>10-second delay before the first run to allow application startup to complete.</summary>
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

        /// <summary>15-minute interval between analysis runs; keeps <c>IsAnalysed=false</c> backlog small.</summary>
        private static readonly TimeSpan RunInterval  = TimeSpan.FromMinutes(15);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecipeAnalysisPipelineBackgroundService> _logger;

        public RecipeAnalysisPipelineBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<RecipeAnalysisPipelineBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger       = logger;
        }

        /// <summary>
        /// Executes the analysis loop. Waits <see cref="StartupDelay"/> (10 s) before the first run,
        /// then processes unanalysed <c>RecipeIndex</c> rows in batches of 10 every 15 minutes.
        /// Handles <see cref="OperationCanceledException"/> gracefully on shutdown.
        /// </summary>
        /// <param name="stoppingToken">Cancellation token signalled by the host on shutdown.</param>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "[RecipeAnalysis] Background service started. " +
                "First run in {Seconds}s; subsequent runs every {Hours}h.",
                StartupDelay.TotalSeconds, RunInterval.TotalHours);

            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);

                _logger.LogInformation("[RecipeAnalysis] Next run in {Hours} hours.", RunInterval.TotalHours);
                await Task.Delay(RunInterval, stoppingToken);
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            _logger.LogInformation("[RecipeAnalysis] Starting analysis run at {Time:u}.", DateTime.UtcNow);

            try
            {
                using var scope    = _scopeFactory.CreateScope();
                var pipeline       = scope.ServiceProvider
                                         .GetRequiredService<IRecipeAnalysisPipelineService>();

                var stats = await pipeline.RunAsync(ct);

                _logger.LogInformation(
                    "[RecipeAnalysis] Run complete. Processed={Processed} Failed={Failed} Time={Time}ms",
                    stats.TotalProcessed, stats.TotalFailed, stats.ElapsedMs);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[RecipeAnalysis] Run cancelled (application shutting down).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[RecipeAnalysis] Unhandled error during analysis run. Will retry in {Hours}h.",
                    RunInterval.TotalHours);
            }
        }
    }
}
