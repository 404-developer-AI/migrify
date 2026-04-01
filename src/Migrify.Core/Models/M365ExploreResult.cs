namespace Migrify.Core.Models;

public record M365ExploreResult(
    bool Success,
    string? ErrorMessage,
    List<M365FolderInfo> Folders);
