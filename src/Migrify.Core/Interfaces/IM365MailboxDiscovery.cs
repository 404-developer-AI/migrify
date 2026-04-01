using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface IM365MailboxDiscovery
{
    Task<List<DiscoveredMailboxDto>> DiscoverMailboxesAsync(string tenantId, string clientId, string clientSecret);
}
