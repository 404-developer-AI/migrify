using Migrify.Core.Entities;
using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface IConcurrencyLimitService
{
    bool CanStartJob(string tenantId, string sourceKey, SourceConnectorType sourceType);
    ConcurrencyLimits GetCurrentLimits();
    void RefreshSystemLimits();
}
