using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Platerly.Api.Services.RecipeIndex
{
    /// <summary>
    /// Runs GeneralRecipeIndexingService on a fixed schedule.
    /// Startup delay: 5 minutes (after TheMealDB indexer completes its first run).
    /// Run interval: every 48 hours.
    /// </summary>
    public class GeneralRecipeIndexingBackgroundService : BackgroundService
    {
        private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RunInterval  = TimeSpan.FromHours(48);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<GeneralRecipeIndexingBackgroundService> _log;

        public GeneralRecipeIndexingBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<GeneralRecipeIndexingBackgroundService> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            _log.LogInformation("[GeneralIndexer] Waiting {Delay} before first run", StartupDelay);
            await Task.Delay(StartupDelay, ct);

            while (!ct.IsCancellationRequested)
            {
                _log.LogInformation("[GeneralIndexer] Starting indexing run");
                var sw = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IGeneralRecipeIndexingService>();

                    // Also seed domains on first run if not already seeded
                    var seeder = scope.ServiceProvider.GetRequiredService<RecipeIndexSourceSeed>();
                    await seeder.SeedAsync(ct);

                    var results = await svc.RunAllAsync(ct);

                    var totalPassed   = results.Sum(r => r.PassedHalalGate);
                    var totalFailed   = results.Sum(r => r.FailedHalalGate);
                    var totalErrors   = results.Sum(r => r.Errors);

                    _log.LogInformation(
                        "[GeneralIndexer] Run complete — {Domains} domains, {Passed} recipes indexed, {Failed} halal failures, {Errors} errors in {Elapsed:F1}s",
                        results.Count, totalPassed, totalFailed, totalErrors, sw.Elapsed.TotalSeconds);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log.LogError(ex, "[GeneralIndexer] Unhandled error during indexing run");
                }

                _log.LogInformation("[GeneralIndexer] Next run in {Interval}", RunInterval);
                await Task.Delay(RunInterval, ct);
            }
        }
    }
}
