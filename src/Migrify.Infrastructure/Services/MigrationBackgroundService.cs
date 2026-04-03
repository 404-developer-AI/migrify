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
    private readonly List<Task> _runningTasks = new();
    private readonly object _taskLock = new();

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

            // Skip jobs that were cancelled from the queue before being picked up
            if (_queue.WasCancelledFromQueue(jobId))
            {
                _logger.LogInformation("Skipping cancelled-from-queue job {JobId}", jobId);
                continue;
            }

            // Clean up completed tasks
            CleanupCompletedTasks();

            // Start the job as a parallel task
            var task = RunJobAsync(jobId, stoppingToken);

            lock (_taskLock)
            {
                _runningTasks.Add(task);
            }
        }

        // Wait for all running tasks to complete on shutdown
        Task[] tasksToWait;
        lock (_taskLock)
        {
            tasksToWait = _runningTasks.ToArray();
        }

        if (tasksToWait.Length > 0)
        {
            _logger.LogInformation("Waiting for {Count} running migration jobs to complete...", tasksToWait.Length);
            await Task.WhenAll(tasksToWait);
        }

        _logger.LogInformation("Migration background service stopped");
    }

    private async Task RunJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        var cts = _queue.RegisterCancellation(jobId);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, stoppingToken);

        try
        {
            _logger.LogInformation("Starting migration job {JobId}", jobId);

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

    private void CleanupCompletedTasks()
    {
        lock (_taskLock)
        {
            _runningTasks.RemoveAll(t => t.IsCompleted);
        }
    }
}
