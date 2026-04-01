using System.Net;
using System.Net.Sockets;
using System.Text;
using Azure.Identity;
using Google.Apis.Auth.OAuth2;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Services;

public class MigrationEngine : IMigrationEngine
{
    private readonly IMigrationJobRepository _jobRepository;
    private readonly ICredentialEncryptor _encryptor;
    private readonly IImapOAuthCredentialProvider _oauthProvider;
    private readonly ILogger<MigrationEngine> _logger;

    private const int MaxMimeSize = 4 * 1024 * 1024; // 4MB Graph API limit for direct upload

    public MigrationEngine(
        IMigrationJobRepository jobRepository,
        ICredentialEncryptor encryptor,
        IImapOAuthCredentialProvider oauthProvider,
        ILogger<MigrationEngine> logger)
    {
        _jobRepository = jobRepository;
        _encryptor = encryptor;
        _oauthProvider = oauthProvider;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetByIdWithProjectAsync(jobId);
        if (job is null)
        {
            _logger.LogError("Migration job {JobId} not found", jobId);
            return;
        }

        var project = job.Project;
        _logger.LogInformation("Starting migration for job {JobId}: {Source} → {Dest}",
            jobId, job.SourceEmail, job.DestinationEmail);

        try
        {
            // Validate preconditions
            var validationError = ValidatePreconditions(job, project);
            if (validationError is not null)
            {
                await SetJobFailed(job, validationError);
                return;
            }

            // Set status to Running
            job.Status = MigrationJobStatus.Running;
            job.ProcessedMessages = 0;
            job.TotalMessages = 0;
            job.CurrentFolder = null;
            job.ErrorMessage = null;
            await _jobRepository.UpdateAsync(job);

            // Decrypt M365 credentials
            var m365 = project.M365Settings!;
            var clientSecret = _encryptor.Decrypt(m365.EncryptedClientSecret!);
            var credential = new ClientSecretCredential(m365.TenantId, m365.ClientId, clientSecret);

            // Get bearer token for Graph API
            var tokenContext = new Azure.Core.TokenRequestContext(["https://graph.microsoft.com/.default"]);
            var token = await credential.GetTokenAsync(tokenContext, cancellationToken);

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            // Count total messages across all mapped folders
            var mappings = job.FolderMappings.OrderBy(m => m.SourceFolderName).ToList();
            var folderMessageCounts = await CountMessagesPerFolder(job, project, mappings, cancellationToken);

            job.TotalMessages = folderMessageCounts.Values.Sum();
            await _jobRepository.UpdateAsync(job);

            _logger.LogInformation("Job {JobId}: {TotalMessages} messages across {FolderCount} folders",
                jobId, job.TotalMessages, mappings.Count);

            if (job.TotalMessages == 0)
            {
                job.Status = MigrationJobStatus.Completed;
                job.CurrentFolder = null;
                await _jobRepository.UpdateAsync(job);
                _logger.LogInformation("Job {JobId}: No messages to migrate, completed", jobId);
                return;
            }

            // Migrate each folder
            int skippedMessages = 0;
            int failedMessages = 0;

            foreach (var mapping in mappings)
            {
                cancellationToken.ThrowIfCancellationRequested();

                job.CurrentFolder = mapping.SourceFolderName;
                await _jobRepository.UpdateAsync(job);

                _logger.LogInformation("Job {JobId}: Migrating folder '{Folder}' → '{DestFolder}'",
                    jobId, mapping.SourceFolderName, mapping.DestinationFolderDisplayName);

                var messageCount = folderMessageCounts.GetValueOrDefault(mapping.SourceFolderName, 0);
                if (messageCount == 0)
                {
                    _logger.LogDebug("Job {JobId}: Folder '{Folder}' is empty, skipping", jobId, mapping.SourceFolderName);
                    continue;
                }

                // Connect to IMAP and migrate messages
                using var imapClient = CreateImapClient();
                try
                {
                    await ConnectAndAuthenticate(imapClient, job, project, cancellationToken);

                    var folder = await imapClient.GetFolderAsync(mapping.SourceFolderName, cancellationToken);
                    await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                    for (int i = 0; i < folder.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            // Refresh token if needed (tokens expire after ~1 hour)
                            if (token.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
                            {
                                token = await credential.GetTokenAsync(tokenContext, cancellationToken);
                                httpClient.DefaultRequestHeaders.Authorization =
                                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
                            }

                            var message = await folder.GetMessageAsync(i, cancellationToken);

                            using var mimeStream = new MemoryStream();
                            await message.WriteToAsync(mimeStream, cancellationToken);

                            if (mimeStream.Length > MaxMimeSize)
                            {
                                _logger.LogWarning("Job {JobId}: Skipping message '{Subject}' in '{Folder}' — size {Size}MB exceeds 4MB limit",
                                    jobId, message.Subject, mapping.SourceFolderName, mimeStream.Length / (1024.0 * 1024.0));
                                skippedMessages++;
                                job.ProcessedMessages++;
                                await _jobRepository.UpdateAsync(job);
                                continue;
                            }

                            // Build Graph API JSON message with MAPI flags to prevent draft status
                            var graphMessage = ConvertToGraphMessage(message);
                            var uploadUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(job.DestinationEmail)}/mailFolders/{mapping.DestinationFolderId}/messages";

                            _logger.LogInformation("Job {JobId}: Uploading message {Index}/{Total} '{Subject}' to '{DestFolder}'",
                                jobId, i + 1, folder.Count, message.Subject, mapping.DestinationFolderDisplayName);

                            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(graphMessage);
                            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                            var response = await httpClient.PostAsync(uploadUrl, content, cancellationToken);

                            if (!response.IsSuccessStatusCode)
                            {
                                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                                _logger.LogWarning("Job {JobId}: Failed to upload message '{Subject}' to '{DestFolder}' — {StatusCode}: {Error}",
                                    jobId, message.Subject, mapping.DestinationFolderDisplayName, response.StatusCode, responseBody[..Math.Min(500, responseBody.Length)]);
                                failedMessages++;

                                // Handle rate limiting
                                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                                {
                                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                                    _logger.LogWarning("Job {JobId}: Rate limited, waiting {Seconds}s", jobId, retryAfter.TotalSeconds);
                                    await Task.Delay(retryAfter, cancellationToken);

                                    using var retryContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                                    response = await httpClient.PostAsync(uploadUrl, retryContent, cancellationToken);
                                    if (response.IsSuccessStatusCode)
                                        failedMessages--;
                                }
                            }
                            else
                            {
                                _logger.LogInformation("Job {JobId}: Successfully migrated '{Subject}' → '{DestFolder}'",
                                    jobId, message.Subject, mapping.DestinationFolderDisplayName);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Job {JobId}: Error processing message {Index} in '{Folder}'",
                                jobId, i, mapping.SourceFolderName);
                            failedMessages++;
                        }

                        job.ProcessedMessages++;
                        await _jobRepository.UpdateAsync(job);
                    }

                    await folder.CloseAsync(false, cancellationToken);
                    await imapClient.DisconnectAsync(true, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Job {JobId}: IMAP error for folder '{Folder}', attempting retry",
                        jobId, mapping.SourceFolderName);

                    // One retry for IMAP connection errors
                    try
                    {
                        if (imapClient.IsConnected)
                            await imapClient.DisconnectAsync(true, cancellationToken);
                    }
                    catch { /* ignore disconnect errors */ }

                    throw; // Let outer catch handle it
                }
            }

            // Completed
            job.Status = MigrationJobStatus.Completed;
            job.CurrentFolder = null;

            if (skippedMessages > 0 || failedMessages > 0)
            {
                job.ErrorMessage = $"Completed with {failedMessages} failed and {skippedMessages} skipped (>4MB) messages.";
            }

            await _jobRepository.UpdateAsync(job);
            _logger.LogInformation("Job {JobId}: Migration completed. {Processed}/{Total} messages. {Failed} failed, {Skipped} skipped.",
                jobId, job.ProcessedMessages, job.TotalMessages, failedMessages, skippedMessages);
        }
        catch (OperationCanceledException)
        {
            job.Status = MigrationJobStatus.Cancelled;
            job.CurrentFolder = null;
            await _jobRepository.UpdateAsync(job);
            _logger.LogInformation("Job {JobId}: Migration cancelled at {Processed}/{Total} messages",
                jobId, job.ProcessedMessages, job.TotalMessages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: Migration failed", jobId);
            await SetJobFailed(job, ex.Message);
        }
    }


    private static Dictionary<string, object?> ConvertToGraphMessage(MimeKit.MimeMessage mime)
    {
        var msg = new Dictionary<string, object?>
        {
            ["subject"] = mime.Subject ?? "(no subject)",
            ["internetMessageId"] = mime.MessageId,
        };

        // Body
        var htmlBody = mime.HtmlBody;
        var textBody = mime.TextBody;
        if (!string.IsNullOrEmpty(htmlBody))
            msg["body"] = new { contentType = "html", content = htmlBody };
        else if (!string.IsNullOrEmpty(textBody))
            msg["body"] = new { contentType = "text", content = textBody };

        // Dates
        if (mime.Date != DateTimeOffset.MinValue)
        {
            msg["sentDateTime"] = mime.Date.UtcDateTime.ToString("o");
            msg["receivedDateTime"] = mime.Date.UtcDateTime.ToString("o");
        }

        // From
        if (mime.From.Mailboxes.Any())
        {
            var from = mime.From.Mailboxes.First();
            msg["from"] = new { emailAddress = new { name = from.Name ?? from.Address, address = from.Address } };
            msg["sender"] = msg["from"];
        }

        // To
        if (mime.To.Mailboxes.Any())
            msg["toRecipients"] = mime.To.Mailboxes.Select(m =>
                new { emailAddress = new { name = m.Name ?? m.Address, address = m.Address } }).ToArray();

        // Cc
        if (mime.Cc.Mailboxes.Any())
            msg["ccRecipients"] = mime.Cc.Mailboxes.Select(m =>
                new { emailAddress = new { name = m.Name ?? m.Address, address = m.Address } }).ToArray();

        // Bcc
        if (mime.Bcc.Mailboxes.Any())
            msg["bccRecipients"] = mime.Bcc.Mailboxes.Select(m =>
                new { emailAddress = new { name = m.Name ?? m.Address, address = m.Address } }).ToArray();

        // Importance
        msg["importance"] = mime.Importance switch
        {
            MimeKit.MessageImportance.High => "high",
            MimeKit.MessageImportance.Low => "low",
            _ => "normal"
        };

        // MAPI extended properties — override Exchange-level message flags and dates.
        // PR_MESSAGE_FLAGS (0x0E07): Value 1 = MSGFLAG_READ, omitting MSGFLAG_UNSENT (8) prevents draft status.
        // PR_CLIENT_SUBMIT_TIME (0x0039): Original sent date.
        // PR_MESSAGE_DELIVERY_TIME (0x0E06): Original received/delivery date.
        var extendedProps = new List<object>
        {
            new { id = "Integer 0x0E07", value = "1" }
        };

        if (mime.Date != DateTimeOffset.MinValue)
        {
            var isoDate = mime.Date.UtcDateTime.ToString("o");
            extendedProps.Add(new { id = "SystemTime 0x0039", value = isoDate });
            extendedProps.Add(new { id = "SystemTime 0x0E06", value = isoDate });
        }

        msg["singleValueExtendedProperties"] = extendedProps;

        // Attachments
        var attachments = new List<Dictionary<string, object?>>();
        foreach (var attachment in mime.Attachments)
        {
            if (attachment is MimeKit.MimePart part)
            {
                using var ms = new MemoryStream();
                part.Content.DecodeTo(ms);
                var bytes = ms.ToArray();

                attachments.Add(new Dictionary<string, object?>
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = part.FileName ?? "attachment",
                    ["contentType"] = part.ContentType.MimeType,
                    ["contentBytes"] = Convert.ToBase64String(bytes)
                });
            }
        }

        if (attachments.Count > 0)
            msg["attachments"] = attachments;

        return msg;
    }

    private string? ValidatePreconditions(MigrationJob job, Project project)
    {
        if (!job.FolderMappings.Any())
            return "No folder mappings configured.";

        if (project.M365Settings is null ||
            string.IsNullOrEmpty(project.M365Settings.TenantId) ||
            string.IsNullOrEmpty(project.M365Settings.ClientId) ||
            string.IsNullOrEmpty(project.M365Settings.EncryptedClientSecret))
            return "M365 destination connector not configured.";

        if (string.IsNullOrEmpty(job.DestinationEmail))
            return "Destination email not set.";

        if (string.IsNullOrEmpty(job.SourceEmail))
            return "Source email not set.";

        // Check IMAP credentials
        if (project.SourceConnectorType == SourceConnectorType.ManualImap || job.HasImapOverride)
        {
            if (job.ImapSettings is null || string.IsNullOrEmpty(job.ImapSettings.Host))
                return "IMAP settings not configured for this job.";
        }
        else if (project.SourceConnectorType == SourceConnectorType.GoogleWorkspace)
        {
            if (project.GoogleWorkspaceSettings is null ||
                string.IsNullOrEmpty(project.GoogleWorkspaceSettings.EncryptedPrivateKey))
                return "Google Workspace settings not configured.";
        }

        return null;
    }

    private async Task<Dictionary<string, int>> CountMessagesPerFolder(
        MigrationJob job, Project project, List<FolderMapping> mappings, CancellationToken ct)
    {
        var counts = new Dictionary<string, int>();

        using var client = CreateImapClient();
        await ConnectAndAuthenticate(client, job, project, ct);

        foreach (var mapping in mappings)
        {
            try
            {
                var folder = await client.GetFolderAsync(mapping.SourceFolderName, ct);
                await folder.OpenAsync(FolderAccess.ReadOnly, ct);
                counts[mapping.SourceFolderName] = folder.Count;
                await folder.CloseAsync(false, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not count messages in folder '{Folder}'", mapping.SourceFolderName);
                counts[mapping.SourceFolderName] = 0;
            }
        }

        await client.DisconnectAsync(true, ct);
        return counts;
    }

    private async Task ConnectAndAuthenticate(ImapClient client, MigrationJob job, Project project, CancellationToken ct)
    {
        if (project.SourceConnectorType == SourceConnectorType.GoogleWorkspace && !job.HasImapOverride)
        {
            // Google Workspace: service account impersonation
            var gws = project.GoogleWorkspaceSettings!;
            var privateKey = _encryptor.Decrypt(gws.EncryptedPrivateKey!);
            var credential = new ServiceAccountCredential(
                new ServiceAccountCredential.Initializer(gws.ServiceAccountEmail)
                {
                    Scopes = ["https://mail.google.com/"],
                    User = job.SourceEmail
                }.FromPrivateKey(privateKey));

            await credential.RequestAccessTokenAsync(ct);
            var accessToken = credential.Token.AccessToken;

            await ConnectImapClient(client, "imap.gmail.com", 993, SecureSocketOptions.SslOnConnect, ct);

            var oauth2 = new SaslMechanismOAuth2(job.SourceEmail, accessToken);
            await client.AuthenticateAsync(oauth2, ct);
        }
        else
        {
            // Manual IMAP (or IMAP override)
            var imap = job.ImapSettings!;
            var socketOptions = MapEncryption(imap.Encryption);

            await ConnectImapClient(client, imap.Host, imap.Port, socketOptions, ct);

            if (imap.AuthType == ImapAuthType.OAuth2)
            {
                var accessToken = await _oauthProvider.GetAccessTokenAsync(imap);
                var oauth2 = new SaslMechanismOAuth2(imap.Username ?? string.Empty, accessToken);
                await client.AuthenticateAsync(oauth2, ct);
            }
            else
            {
                var password = _encryptor.Decrypt(imap.EncryptedPassword!);
                await client.AuthenticateAsync(imap.Username ?? string.Empty, password, ct);
            }
        }
    }

    private async Task ConnectImapClient(ImapClient client, string host, int port, SecureSocketOptions socketOptions, CancellationToken ct)
    {
        try
        {
            await client.ConnectAsync(host, port, socketOptions, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Primary IMAP connection failed to {Host}:{Port}, attempting IPv4 fallback", host, port);

            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new InvalidOperationException($"No IPv4 address found for {host}");

            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(ipv4, port, ct);
            await client.ConnectAsync(socket, host, port, socketOptions, ct);
        }
    }

    private async Task SetJobFailed(MigrationJob job, string errorMessage)
    {
        job.Status = MigrationJobStatus.Failed;
        job.ErrorMessage = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;
        job.CurrentFolder = null;
        await _jobRepository.UpdateAsync(job);
        _logger.LogError("Job {JobId}: Failed — {Error}", job.Id, errorMessage);
    }

    private static ImapClient CreateImapClient()
    {
        var client = new ImapClient();
        client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        client.CheckCertificateRevocation = false;
        client.SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                            | System.Security.Authentication.SslProtocols.Tls13;
        return client;
    }

    private static SecureSocketOptions MapEncryption(ImapEncryption encryption) => encryption switch
    {
        ImapEncryption.SSL => SecureSocketOptions.SslOnConnect,
        ImapEncryption.TLS => SecureSocketOptions.SslOnConnect,
        ImapEncryption.STARTTLS => SecureSocketOptions.StartTls,
        ImapEncryption.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto
    };
}
