namespace Migrify.Core.Entities;

public class FolderMapping
{
    public Guid Id { get; set; }
    public Guid MigrationJobId { get; set; }
    public string SourceFolderName { get; set; } = string.Empty;
    public string DestinationFolderId { get; set; } = string.Empty;
    public string DestinationFolderDisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public MigrationJob MigrationJob { get; set; } = null!;
}
