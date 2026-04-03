using Migrify.Core.Entities;

namespace Migrify.Core.Models;

public record RunningJobInfo(
    Guid JobId,
    string TenantId,
    string SourceKey,
    SourceConnectorType SourceType
);
