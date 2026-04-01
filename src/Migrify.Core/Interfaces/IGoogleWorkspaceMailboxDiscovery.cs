using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface IGoogleWorkspaceMailboxDiscovery
{
    Task<ConnectionTestResult> TestConnectionAsync(string serviceAccountEmail, string privateKey, string tokenUri, string impersonationEmail, string domain);
    Task<List<DiscoveredMailboxDto>> DiscoverMailboxesAsync(string serviceAccountEmail, string privateKey, string tokenUri, string impersonationEmail, string domain);
}
