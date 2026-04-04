using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface IMigrationQueueService
{
    ValueTask EnqueueAsync(Guid jobId);
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
    bool TryCancel(Guid jobId);
    bool IsRunning(Guid jobId);
    bool IsQueued(Guid jobId);
    bool TryCancelQueued(Guid jobId);
    bool WasUserCancelled(Guid jobId);

    // Metadata tracking for concurrency limits
    void RegisterJobMetadata(Guid jobId, RunningJobInfo info);
    IReadOnlyList<RunningJobInfo> GetRunningJobInfos();
    int CountRunningByTenant(string tenantId);
    int CountRunningBySource(string sourceKey);
}
