using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;

namespace Migrify.Infrastructure.Services;

public class M365ConnectionTester(ILogger<M365ConnectionTester> logger) : IM365ConnectionTester
{
    public async Task<ConnectionTestResult> TestAsync(string tenantId, string clientId, string clientSecret)
    {
        try
        {
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            var graphClient = new GraphServiceClient(credential);

            await graphClient.Organization.GetAsync();

            return new ConnectionTestResult(true);
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            logger.LogWarning(ex, "M365 credential validation failed for tenant {TenantId}", tenantId);
            return new ConnectionTestResult(false, message);
        }
    }

    public async Task<ConnectionTestResult> TestAsync(string tenantId, string clientId, string clientSecret, string destinationEmail)
    {
        try
        {
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            var graphClient = new GraphServiceClient(credential);

            // Step 1: Validate credentials
            logger.LogDebug("Testing M365 credentials for tenant {TenantId}", tenantId);
            await graphClient.Organization.GetAsync();
            logger.LogDebug("M365 credentials valid for tenant {TenantId}", tenantId);

            // Step 2: Validate Mail.ReadWrite permission by reading mailbox folders
            logger.LogDebug("Testing Mail.ReadWrite permission for {Email}", destinationEmail);
            var folders = await graphClient.Users[destinationEmail].MailFolders.GetAsync(config =>
            {
                config.QueryParameters.Top = 1;
            });

            logger.LogInformation("M365 connection and permissions verified for {Email} in tenant {TenantId}", destinationEmail, tenantId);
            return new ConnectionTestResult(true);
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;

            if (message.Contains("Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Insufficient privileges", StringComparison.OrdinalIgnoreCase)
                || message.Contains("MailboxNotEnabledForRESTAPI", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("M365 credentials valid but missing Mail.ReadWrite permission for {Email}", destinationEmail);
                return new ConnectionTestResult(false,
                    $"Credentials are valid, but the app lacks Mail.ReadWrite permission for {destinationEmail}. " +
                    "Ensure the permission is granted and admin consent is provided in Azure AD.");
            }

            logger.LogWarning(ex, "M365 connection test failed for {Email} in tenant {TenantId}", destinationEmail, tenantId);
            return new ConnectionTestResult(false, message);
        }
    }
}
