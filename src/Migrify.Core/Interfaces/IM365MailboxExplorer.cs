using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface IM365MailboxExplorer
{
    Task<M365ExploreResult> ExploreAsync(string tenantId, string clientId, string clientSecret, string userEmail);
}
