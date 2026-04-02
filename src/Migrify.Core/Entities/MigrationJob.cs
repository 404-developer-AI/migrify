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

    // Migration options
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public bool SkipDuplicates { get; set; } = true;
    public MigrationMode MigrationMode { get; set; } = MigrationMode.Copy;
    public bool MigrationOptionsConfigured { get; set; }

    // Migration progress tracking
    public int TotalMessages { get; set; }
    public int ProcessedMessages { get; set; }
    public int SkippedMessages { get; set; }
    public int DuplicateMessages { get; set; }
    public string? CurrentFolder { get; set; }
    public string? ErrorMessage { get; set; }

    public Project Project { get; set; } = null!;
    public ImapSettings? ImapSettings { get; set; }
    public ICollection<FolderMapping> FolderMappings { get; set; } = new List<FolderMapping>();
}
