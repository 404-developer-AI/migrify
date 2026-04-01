using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Migrify.Core.Interfaces;
using Migrify.Core.Models;

namespace Migrify.Infrastructure.Services;

public class M365MailboxExplorer(ILogger<M365MailboxExplorer> logger) : IM365MailboxExplorer
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CreateTimeout = TimeSpan.FromSeconds(30);

    public async Task<M365ExploreResult> ExploreAsync(string tenantId, string clientId, string clientSecret, string userEmail)
    {
        using var cts = new CancellationTokenSource(Timeout);

        try
        {
            logger.LogDebug("Starting M365 mailbox exploration for {Email} in tenant {TenantId}", userEmail, tenantId);

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            var graphClient = new GraphServiceClient(credential);

            var folders = new List<M365FolderInfo>();
            var pageResponse = await graphClient.Users[userEmail].MailFolders.GetAsync(config =>
            {
                config.QueryParameters.Top = 100;
                config.QueryParameters.Select = ["id", "displayName", "totalItemCount", "unreadItemCount"];
            }, cts.Token);

            if (pageResponse?.Value is null)
            {
                return new M365ExploreResult(true, null, folders);
            }

            // Collect all folders (with paging)
            var allFolders = new List<MailFolder>();
            allFolders.AddRange(pageResponse.Value);

            var nextLink = pageResponse.OdataNextLink;
            while (!string.IsNullOrEmpty(nextLink))
            {
                cts.Token.ThrowIfCancellationRequested();
                var nextPage = await graphClient.Users[userEmail].MailFolders
                    .WithUrl(nextLink)
                    .GetAsync(cancellationToken: cts.Token);

                if (nextPage?.Value is not null)
                    allFolders.AddRange(nextPage.Value);

                nextLink = nextPage?.OdataNextLink;
            }

            logger.LogDebug("Found {Count} mail folders for {Email}", allFolders.Count, userEmail);

            // Get last message date per folder
            foreach (var folder in allFolders)
            {
                cts.Token.ThrowIfCancellationRequested();

                DateTime? lastMessageDate = null;
                var totalItems = folder.TotalItemCount ?? 0;

                if (totalItems > 0)
                {
                    try
                    {
                        var messages = await graphClient.Users[userEmail]
                            .MailFolders[folder.Id]
                            .Messages
                            .GetAsync(config =>
                            {
                                config.QueryParameters.Top = 1;
                                config.QueryParameters.Orderby = ["receivedDateTime desc"];
                                config.QueryParameters.Select = ["receivedDateTime"];
                            }, cts.Token);

                        if (messages?.Value?.Count > 0)
                        {
                            lastMessageDate = messages.Value[0].ReceivedDateTime?.DateTime;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Could not retrieve last message date for folder {Folder}", folder.DisplayName);
                    }
                }

                var displayName = folder.DisplayName ?? "(unnamed)";
                folders.Add(new M365FolderInfo(
                    folder.Id ?? string.Empty,
                    displayName,
                    totalItems,
                    folder.UnreadItemCount ?? 0,
                    lastMessageDate,
                    ParentId: null,
                    DisplayPath: displayName));

                // Load child folders (1 level deep)
                try
                {
                    var childResponse = await graphClient.Users[userEmail]
                        .MailFolders[folder.Id]
                        .ChildFolders
                        .GetAsync(config =>
                        {
                            config.QueryParameters.Top = 100;
                            config.QueryParameters.Select = ["id", "displayName", "totalItemCount", "unreadItemCount"];
                        }, cts.Token);

                    if (childResponse?.Value is not null)
                    {
                        foreach (var child in childResponse.Value)
                        {
                            var childName = child.DisplayName ?? "(unnamed)";
                            folders.Add(new M365FolderInfo(
                                child.Id ?? string.Empty,
                                childName,
                                child.TotalItemCount ?? 0,
                                child.UnreadItemCount ?? 0,
                                null,
                                ParentId: folder.Id,
                                DisplayPath: $"{displayName}/{childName}"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Could not retrieve child folders for {Folder}", displayName);
                }
            }

            logger.LogInformation("M365 exploration complete for {Email}: {FolderCount} folders (incl. subfolders), {TotalItems} total items",
                userEmail, folders.Count, folders.Sum(f => f.TotalItemCount));

            return new M365ExploreResult(true, null, folders);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("M365 exploration timed out for {Email}", userEmail);
            return new M365ExploreResult(false, "Exploration timed out after 60 seconds.", []);
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            logger.LogError(ex, "M365 exploration failed for {Email}", userEmail);
            return new M365ExploreResult(false, message, []);
        }
    }

    public async Task<M365FolderInfo> CreateFolderAsync(string tenantId, string clientId, string clientSecret, string userEmail, string folderDisplayName, string? parentFolderId = null)
    {
        using var cts = new CancellationTokenSource(CreateTimeout);

        logger.LogDebug("Creating M365 mail folder '{FolderName}' (parent: {ParentId}) for {Email}",
            folderDisplayName, parentFolderId ?? "top-level", userEmail);

        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var graphClient = new GraphServiceClient(credential);

        MailFolder? newFolder;
        if (string.IsNullOrEmpty(parentFolderId))
        {
            newFolder = await graphClient.Users[userEmail].MailFolders.PostAsync(
                new MailFolder { DisplayName = folderDisplayName }, cancellationToken: cts.Token);
        }
        else
        {
            newFolder = await graphClient.Users[userEmail].MailFolders[parentFolderId].ChildFolders.PostAsync(
                new MailFolder { DisplayName = folderDisplayName }, cancellationToken: cts.Token);
        }

        logger.LogInformation("Created M365 mail folder '{FolderName}' (Id: {FolderId}) for {Email}",
            folderDisplayName, newFolder?.Id, userEmail);

        return new M365FolderInfo(
            newFolder?.Id ?? string.Empty,
            newFolder?.DisplayName ?? folderDisplayName,
            newFolder?.TotalItemCount ?? 0,
            newFolder?.UnreadItemCount ?? 0,
            null,
            ParentId: parentFolderId,
            DisplayPath: null); // DisplayPath wordt door de caller gezet (kent de parent naam)
    }
}
