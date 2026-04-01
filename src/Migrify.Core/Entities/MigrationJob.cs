namespace Migrify.Core.Entities;

public class MigrationJob
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string SourceEmail { get; set; } = string.Empty;
    public string DestinationEmail { get; set; } = string.Empty;
    public MigrationJobStatus Status { get; set; } = MigrationJobStatus.New;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool HasImapOverride { get; set; }

    public Project Project { get; set; } = null!;
    public ImapSettings? ImapSettings { get; set; }
}
