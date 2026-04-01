using Google.Apis.Admin.Directory.directory_v1;
using Google.Apis.Admin.Directory.directory_v1.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;

namespace Migrify.Infrastructure.Services;

public class GoogleWorkspaceMailboxDiscovery(ILogger<GoogleWorkspaceMailboxDiscovery> logger) : IGoogleWorkspaceMailboxDiscovery
{
    public async Task<ConnectionTestResult> TestConnectionAsync(
        string serviceAccountEmail, string privateKey, string tokenUri,
        string impersonationEmail, string domain)
    {
        try
        {
            var service = CreateDirectoryService(serviceAccountEmail, privateKey, impersonationEmail);

            var request = service.Users.List();
            request.Domain = domain;
            request.MaxResults = 1;

            await request.ExecuteAsync();

            logger.LogInformation("Google Workspace connection test successful for domain {Domain}", domain);
            return new ConnectionTestResult(true);
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            logger.LogWarning(ex, "Google Workspace connection test failed for domain {Domain}", domain);
            return new ConnectionTestResult(false, message);
        }
    }

    public async Task<List<DiscoveredMailboxDto>> DiscoverMailboxesAsync(
        string serviceAccountEmail, string privateKey, string tokenUri,
        string impersonationEmail, string domain)
    {
        var service = CreateDirectoryService(serviceAccountEmail, privateKey, impersonationEmail);
        var mailboxes = new List<DiscoveredMailboxDto>();

        logger.LogDebug("Discovering Google Workspace mailboxes for domain {Domain}", domain);

        string? pageToken = null;
        do
        {
            var request = service.Users.List();
            request.Domain = domain;
            request.MaxResults = 500;
            request.OrderBy = UsersResource.ListRequest.OrderByEnum.Email;
            request.PageToken = pageToken;

            var response = await request.ExecuteAsync();

            if (response.UsersValue is not null)
            {
                foreach (var user in response.UsersValue)
                {
                    if (!string.IsNullOrWhiteSpace(user.PrimaryEmail))
                    {
                        mailboxes.Add(new DiscoveredMailboxDto(user.PrimaryEmail, user.Name?.FullName));
                    }
                }
            }

            pageToken = response.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        logger.LogInformation("Discovered {Count} mailboxes in Google Workspace domain {Domain}", mailboxes.Count, domain);
        return mailboxes;
    }

    private static DirectoryService CreateDirectoryService(string serviceAccountEmail, string privateKey, string impersonationEmail)
    {
        var credential = new ServiceAccountCredential(
            new ServiceAccountCredential.Initializer(serviceAccountEmail)
            {
                Scopes = ["https://www.googleapis.com/auth/admin.directory.user.readonly"],
                User = impersonationEmail
            }.FromPrivateKey(privateKey));

        return new DirectoryService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Migrify"
        });
    }
}
