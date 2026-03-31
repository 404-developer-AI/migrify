using Migrify.Core.Entities;

namespace Migrify.Core.Interfaces;

public interface IImapOAuthCredentialProvider
{
    Task<string> GetAccessTokenAsync(ImapSettings settings);
}
