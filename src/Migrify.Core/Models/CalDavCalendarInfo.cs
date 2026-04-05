namespace Migrify.Core.Models;

public record CalDavCalendarInfo(
    string DisplayName,
    string Path,
    string? Color,
    int EventCount
);
