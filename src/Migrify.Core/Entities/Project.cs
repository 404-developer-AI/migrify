namespace Migrify.Core.Entities;

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.New;
    public SourceConnectorType SourceConnectorType { get; set; } = SourceConnectorType.ManualImap;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<MigrationJob> MigrationJobs { get; set; } = new List<MigrationJob>();
    public M365Settings? M365Settings { get; set; }
    public GoogleWorkspaceSettings? GoogleWorkspaceSettings { get; set; }
    public ICollection<DiscoveredMailbox> DiscoveredMailboxes { get; set; } = new List<DiscoveredMailbox>();

    public ProjectStatus ComputedStatus
    {
        get
        {
            if (MigrationJobs.Count == 0)
                return ProjectStatus.New;

            if (MigrationJobs.Any(j => j.Status is MigrationJobStatus.Running or MigrationJobStatus.Queued))
                return ProjectStatus.Active;

            if (MigrationJobs.All(j => j.Status is MigrationJobStatus.Completed or MigrationJobStatus.Cancelled))
                return ProjectStatus.Completed;

            if (MigrationJobs.Any(j => j.Status is MigrationJobStatus.Completed or MigrationJobStatus.Failed))
                return ProjectStatus.Active;

            return ProjectStatus.New;
        }
    }
}
