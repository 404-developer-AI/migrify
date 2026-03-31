using Migrify.Core.Entities;
using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface IImapMailboxExplorer
{
    Task<ImapExploreResult> ExploreAsync(
        string host, int port, ImapEncryption encryption,
        string username, string password,
        CancellationToken cancellationToken = default);
}
