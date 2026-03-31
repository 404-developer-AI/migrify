using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface IImapAutoDiscoveryService
{
    Task<ImapAutoDiscoveryResult?> DiscoverAsync(string emailAddress, CancellationToken cancellationToken = default);
}
