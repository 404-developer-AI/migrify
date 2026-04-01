using Migrify.Core.Models;

namespace Migrify.Core.Interfaces;

public interface IM365ConnectionTester
{
    Task<ConnectionTestResult> TestAsync(string tenantId, string clientId, string clientSecret);
    Task<ConnectionTestResult> TestAsync(string tenantId, string clientId, string clientSecret, string destinationEmail);
}
