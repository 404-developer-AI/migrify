namespace Migrify.Infrastructure.Services;

public class DestinationLimitProvider
{
    // Graph API: 10K req/10min per app per tenant
    // Each job uses ~4200 req/10min at 150ms rate limit
    // Safe default: 2 concurrent jobs per tenant
    private const int DefaultPerTenantLimit = 2;

    public int GetDestinationLimit(string tenantId)
    {
        return DefaultPerTenantLimit;
    }
}
