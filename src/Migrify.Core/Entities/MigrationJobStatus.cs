namespace Migrify.Core.Entities;

public enum MigrationJobStatus
{
    New,
    Ready,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}
