using Azure.Identity;
using Microsoft.Graph;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;

namespace Migrify.Infrastructure.Services;

public class M365ConnectionTester : IM365ConnectionTester
{
    public async Task<ConnectionTestResult> TestAsync(string tenantId, string clientId, string clientSecret)
    {
        try
        {
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            var graphClient = new GraphServiceClient(credential);

            // Validate credentials by requesting organization info
            // This only verifies the credentials are valid, not specific permissions
            await graphClient.Organization.GetAsync();

            return new ConnectionTestResult(true);
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return new ConnectionTestResult(false, message);
        }
    }
}
