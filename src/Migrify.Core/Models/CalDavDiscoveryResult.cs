using Migrify.Core.Entities;

namespace Migrify.Core.Models;

public record CalDavDiscoveryResult(
    CalDavSupportStatus Status,
    string? BaseUrl,
    string? ProviderName
);
