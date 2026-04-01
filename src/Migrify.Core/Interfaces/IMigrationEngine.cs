namespace Migrify.Core.Interfaces;

public interface IMigrationEngine
{
    Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken);
}
