using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface ICalDavExplorer
{
    Task<CalDavExploreResult> ExploreAsync(
        string baseUrl, string username, string password,
        CancellationToken cancellationToken = default);
}
