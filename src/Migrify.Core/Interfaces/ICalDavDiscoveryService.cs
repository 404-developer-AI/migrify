using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface ICalDavDiscoveryService
{
    Task<CalDavDiscoveryResult> DiscoverAsync(string emailAddress, CancellationToken cancellationToken = default);
}
