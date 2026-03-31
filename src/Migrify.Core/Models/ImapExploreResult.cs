namespace Migrify.Core.Models;

public record ImapExploreResult(
    bool Success,
    string? ErrorMessage,
    string? ServerAddress,
    string? ResolvedIpAddress,
    List<ImapFolderInfo> Folders
);
