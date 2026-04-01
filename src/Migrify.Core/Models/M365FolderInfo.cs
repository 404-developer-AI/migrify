namespace Migrify.Core.Models;

public record M365FolderInfo(
    string Id,
    string DisplayName,
    int TotalItemCount,
    int UnreadItemCount,
    DateTime? LastMessageDate);
