using Migrify.Core.Entities;
using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface IImapConnectionTester
{
    Task<ConnectionTestResult> TestAsync(string host, int port, ImapEncryption encryption, string? username, string? password);
}
