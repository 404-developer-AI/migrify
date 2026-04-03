namespace Migrify.Core.Entities;

public class MigrationLog
{
    public Guid Id { get; set; }
    public Guid MigrationJobId { get; set; }
    public MigrationLogType Type { get; set; }

    // Per-message fields (null for Summary entries)
    public string? Subject { get; set; }
    public DateTime? MessageDate { get; set; }
    public string? SourceFolder { get; set; }
    public string? ErrorMessage { get; set; }

    // Retry & resume fields (null for Summary/Skipped entries)
    public string? InternetMessageId { get; set; }
    public uint? SourceUid { get; set; }
    public string? DestinationFolderId { get; set; }

    // Summary fields (null for Error/Skipped entries)
    public int? TotalProcessed { get; set; }
    public int? TotalFailed { get; set; }
    public int? TotalSkipped { get; set; }
    public int? TotalDuplicates { get; set; }

    public DateTime CreatedAt { get; set; }

    public MigrationJob MigrationJob { get; set; } = null!;
}
