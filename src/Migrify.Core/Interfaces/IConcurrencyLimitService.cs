using Migrify.Core.Entities;
using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface IConcurrencyLimitService
{
    bool CanStartJob(string tenantId, string sourceKey, SourceConnectorType sourceType);
    WaitReason DetermineWaitReason(string tenantId, string sourceKey, SourceConnectorType sourceType);
    ConcurrencyLimits GetCurrentLimits();
    ConcurrencyOverview GetConcurrencyOverview(string? tenantId, string? sourceKey, SourceConnectorType? sourceType);
    void RefreshSystemLimits();
}
