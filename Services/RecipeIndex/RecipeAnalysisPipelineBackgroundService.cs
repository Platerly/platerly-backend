namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Hosted background service that runs the recipe analysis pipeline on a schedule.
    ///
    /// First run: executes 45 seconds after startup (after the indexer's 15-second
    /// startup delay) to process any recipes already in the index.
    ///
    /// Subsequent runs: every 6 hours, picking up any new recipes added by the
    /// 24-hour indexer cycle.
    ///
    /// Uses IServiceScopeFactory to create a fresh DI scope per run because
    /// RecipeAnalysisPipelineService depends on scoped services (PlaterlyDbContext).
    /// </summary>
    public class RecipeAnalysisPipelineBackgroundService : BackgroundService
    {
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);
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
