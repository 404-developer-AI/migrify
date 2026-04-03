namespace Migrify.Core.Interfaces;

public interface IMigrationRetryService
{
    Task<bool> RetryAsync(Guid logId, CancellationToken cancellationToken = default);
    Task<(int Succeeded, int Failed)> RetryAllFailedAsync(Guid jobId, CancellationToken cancellationToken = default);
}
