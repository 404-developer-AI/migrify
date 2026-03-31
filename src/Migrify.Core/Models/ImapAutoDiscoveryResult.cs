using Migrify.Core.Entities;

namespace Migrify.Core.Models;

public record ImapAutoDiscoveryResult(
    string Host,
    int Port,
    ImapEncryption Encryption,
    string? ProviderName = null);
