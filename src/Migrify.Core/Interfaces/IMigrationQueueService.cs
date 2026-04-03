namespace Migrify.Core.Interfaces;

public interface IMigrationQueueService
{
    ValueTask EnqueueAsync(Guid jobId);
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
    bool TryCancel(Guid jobId);
    bool IsRunning(Guid jobId);
    bool IsQueued(Guid jobId);
    bool TryCancelQueued(Guid jobId);
}
