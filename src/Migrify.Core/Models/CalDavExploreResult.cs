namespace Migrify.Core.Models;

public record CalDavExploreResult(
    bool Success,
    string? ErrorMessage,
    List<CalDavCalendarInfo> Calendars
);
