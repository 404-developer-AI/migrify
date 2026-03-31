namespace Migrify.Core.Models;

public record ImapFolderInfo(
    string FullName,
    string Name,
    int MessageCount,
    DateTime? FirstMessageDate,
    DateTime? LastMessageDate
);
