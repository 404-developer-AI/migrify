using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Services;

public class MigrationBackgroundService : BackgroundService
{
    private readonly MigrationQueueService _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MigrationBackgroundService> _logger;

    public MigrationBackgroundService(
        MigrationQueueService queue,
        IServiceScopeFactory scopeFactory,
        ILogger<MigrationBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Migration background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid jobId;
            try
            {
                jobId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var cts = _queue.RegisterCancellation(jobId);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, stoppingToken);

            try
            {
                _logger.LogInformation("Dequeued migration job {JobId}", jobId);

                using var scope = _scopeFactory.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<IMigrationEngine>();
                await engine.ExecuteAsync(jobId, linkedCts.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Migration job {JobId} interrupted by application shutdown", jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in migration job {JobId}", jobId);
            }
            finally
            {
                _queue.RemoveJob(jobId);
            }
        }

        _logger.LogInformation("Migration background service stopped");
    }
}
