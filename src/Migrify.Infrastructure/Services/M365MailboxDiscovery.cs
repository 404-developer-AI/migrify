using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;

namespace Migrify.Infrastructure.Services;

public class M365MailboxDiscovery(ILogger<M365MailboxDiscovery> logger) : IM365MailboxDiscovery
{
    public async Task<List<DiscoveredMailboxDto>> DiscoverMailboxesAsync(string tenantId, string clientId, string clientSecret)
    {
        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var graphClient = new GraphServiceClient(credential);

        var mailboxes = new List<DiscoveredMailboxDto>();

        logger.LogDebug("Discovering M365 mailboxes for tenant {TenantId}", tenantId);

        var response = await graphClient.Users.GetAsync(config =>
        {
            config.QueryParameters.Select = ["displayName", "mail", "userPrincipalName"];
            config.QueryParameters.Top = 999;
            config.Headers.Add("ConsistencyLevel", "eventual");
        });

        var pageIterator = Microsoft.Graph.PageIterator<Microsoft.Graph.Models.User, Microsoft.Graph.Models.UserCollectionResponse>
            .CreatePageIterator(graphClient, response!, user =>
            {
                var email = user.Mail ?? user.UserPrincipalName;
                if (!string.IsNullOrWhiteSpace(email))
                {
                    mailboxes.Add(new DiscoveredMailboxDto(email, user.DisplayName));
                }
                return true;
            });

        await pageIterator.IterateAsync();

        logger.LogInformation("Discovered {Count} mailboxes in M365 tenant {TenantId}", mailboxes.Count, tenantId);
        return mailboxes;
    }
}
