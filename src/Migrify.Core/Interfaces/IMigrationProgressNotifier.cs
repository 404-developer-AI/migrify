namespace Migrify.Core.Interfaces;

public interface IMigrationProgressNotifier
{
    Task SendProgressAsync(Guid projectId, Guid jobId, int processed, int total, string? currentFolder, int skipped, int duplicates);
    Task SendStatusChangeAsync(Guid projectId, Guid jobId, string status, string? errorMessage);
    Task SendJobCompletedAsync(Guid projectId, Guid jobId, int processed, int total, int failed, int skipped, string? errorMessage);
}
