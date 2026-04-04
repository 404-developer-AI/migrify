using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;
using Migrify.Infrastructure.Data;

namespace Migrify.Infrastructure.Services;

public class MigrationBackgroundService : BackgroundService
{
    private readonly MigrationQueueService _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConcurrencyLimitService _limitService;
    private readonly ILogger<MigrationBackgroundService> _logger;
    private readonly List<Task> _runningTasks = new();
    private readonly object _taskLock = new();
    private readonly SemaphoreSlim _jobCompletedSignal = new(0);
    private readonly List<(Guid JobId, RunningJobInfo Info, WaitReason Reason)> _waitingJobs = new();
    private readonly object _waitingLock = new();

    public MigrationBackgroundService(
        MigrationQueueService queue,
        IServiceScopeFactory scopeFactory,
        IConcurrencyLimitService limitService,
        ILogger<MigrationBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _limitService = limitService;
        _logger = logger;
    }

    public IReadOnlyList<(Guid JobId, RunningJobInfo Info, WaitReason Reason)> GetWaitingJobs()
    {
        lock (_waitingLock)
        {
            return _waitingJobs.ToList();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Migration background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            // First, try to dispatch any waiting jobs that might now be eligible
            lock (_waitingLock)
            {
                TryDispatchWaitingJobs(stoppingToken);
            }

            // Clean up completed tasks
            CleanupCompletedTasks();

            // Try to dequeue with a short timeout so we periodically re-check waiting jobs
            Guid jobId;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

                jobId = await _queue.DequeueAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Timeout — no new jobs, but we'll re-check waiting jobs on next iteration
                // If there are waiting jobs, wait for a signal that a running job completed
                int waitingCount;
                lock (_waitingLock) { waitingCount = _waitingJobs.Count; }
                if (waitingCount > 0)
                {
                    try
                    {
                        await _jobCompletedSignal.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                continue;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Skip jobs cancelled from queue
            if (_queue.WasCancelledFromQueue(jobId))
            {
                _logger.LogInformation("Skipping cancelled-from-queue job {JobId}", jobId);
                continue;
            }

            // Load job metadata to determine which tenant/source this job targets
            var jobInfo = await LoadJobMetadataAsync(jobId);
            if (jobInfo is null)
            {
                _logger.LogError("Could not load metadata for job {JobId}, skipping", jobId);
                continue;
            }

            // Check if we can start this job right now
            if (_limitService.CanStartJob(jobInfo.TenantId, jobInfo.SourceKey, jobInfo.SourceType))
            {
                StartJob(jobId, jobInfo, stoppingToken);
            }
            else
            {
                var reason = _limitService.DetermineWaitReason(jobInfo.TenantId, jobInfo.SourceKey, jobInfo.SourceType);
                lock (_waitingLock)
                {
                    _waitingJobs.Add((jobId, jobInfo, reason));
                }
                _logger.LogInformation(
                    "Job {JobId} added to waiting list (tenant: {TenantId}, source: {SourceKey}, reason: {Reason}). {WaitingCount} jobs waiting",
                    jobId, jobInfo.TenantId, jobInfo.SourceKey, reason, _waitingJobs.Count);
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

    private void TryDispatchWaitingJobs(CancellationToken stoppingToken)
    {
        for (int i = _waitingJobs.Count - 1; i >= 0; i--)
        {
            var (jobId, info, _) = _waitingJobs[i];

            if (_limitService.CanStartJob(info.TenantId, info.SourceKey, info.SourceType))
            {
                _waitingJobs.RemoveAt(i);
                StartJob(jobId, info, stoppingToken);
                _logger.LogInformation("Dispatched waiting job {JobId} (tenant: {TenantId}, source: {SourceKey})",
                    jobId, info.TenantId, info.SourceKey);
            }
            else
            {
                // Update wait reason — bottleneck may have changed
                var newReason = _limitService.DetermineWaitReason(info.TenantId, info.SourceKey, info.SourceType);
                _waitingJobs[i] = (jobId, info, newReason);
            }
        }
    }

    private void StartJob(Guid jobId, RunningJobInfo info, CancellationToken stoppingToken)
    {
        _queue.RegisterJobMetadata(jobId, info);

        var task = RunJobAsync(jobId, stoppingToken);

        lock (_taskLock)
        {
            _runningTasks.Add(task);
        }
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
            // Signal the dispatch loop that a slot freed up
            _jobCompletedSignal.Release();
        }
    }

    private async Task<RunningJobInfo?> LoadJobMetadataAsync(Guid jobId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var result = await db.MigrationJobs
                .AsNoTracking()
                .Where(j => j.Id == jobId)
                .Select(j => new
                {
                    j.Id,
                    TenantId = j.Project.M365Settings != null ? j.Project.M365Settings.TenantId : "",
                    SourceConnectorType = j.Project.SourceConnectorType,
                    ImapHost = j.ImapSettings != null ? j.ImapSettings.Host : null,
                    GoogleDomain = j.Project.GoogleWorkspaceSettings != null ? j.Project.GoogleWorkspaceSettings.Domain : null,
                    j.HasImapOverride
                })
                .FirstOrDefaultAsync();

            if (result is null) return null;

            // Determine source key
            string sourceKey;
            var sourceType = result.SourceConnectorType;

            if (sourceType == SourceConnectorType.GoogleWorkspace && !result.HasImapOverride)
            {
                sourceKey = (result.GoogleDomain ?? "").ToLowerInvariant();
            }
            else
            {
                sourceKey = (result.ImapHost ?? "").ToLowerInvariant();
            }

            return new RunningJobInfo(
                JobId: result.Id,
                TenantId: result.TenantId ?? "",
                SourceKey: sourceKey,
                SourceType: sourceType
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load metadata for job {JobId}", jobId);
            return null;
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
