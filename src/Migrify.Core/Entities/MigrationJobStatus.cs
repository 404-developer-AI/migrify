namespace Migrify.Core.Entities;

public enum MigrationJobStatus
{
    New,
    Ready,
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}
