using System.Net;
using System.Text;
using Azure.Identity;
using MailKit;
using Microsoft.Extensions.Logging;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Services;

public class MigrationEngine : IMigrationEngine
{
    private readonly IMigrationJobRepository _jobRepository;
    private readonly IMigrationLogRepository _logRepository;
    private readonly ICredentialEncryptor _encryptor;
    private readonly IImapOAuthCredentialProvider _oauthProvider;
    private readonly IMigrationProgressNotifier _progressNotifier;
    private readonly ILogger<MigrationEngine> _logger;

    private const int DbPersistInterval = 10; // Persist to DB every N messages
    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromMilliseconds(150); // ~7 msg/sec with duplicate check

    public MigrationEngine(
        IMigrationJobRepository jobRepository,
        IMigrationLogRepository logRepository,
        ICredentialEncryptor encryptor,
        IImapOAuthCredentialProvider oauthProvider,
        IMigrationProgressNotifier progressNotifier,
        ILogger<MigrationEngine> logger)
    {
        _jobRepository = jobRepository;
        _logRepository = logRepository;
        _encryptor = encryptor;
        _oauthProvider = oauthProvider;
        _progressNotifier = progressNotifier;
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

        var logBuffer = new List<MigrationLog>();

        try
        {
            // Validate preconditions
            var validationError = ValidatePreconditions(job, project);
            if (validationError is not null)
            {
                await SetJobFailed(job, validationError);
                return;
            }

            // Incremental mode always forces duplicate checking
            bool checkDuplicates = job.SkipDuplicates || job.MigrationMode == MigrationMode.Incremental;

            // Resume detection: check if folder mappings have checkpoint UIDs
            bool isResume = job.FolderMappings.Any(m => m.LastProcessedUid.HasValue);
            if (isResume)
            {
                _logger.LogInformation("Job {JobId}: Resuming from checkpoint — skipping already-processed UIDs per folder", jobId);
            }

            // Set status to Running and reset counters
            job.Status = MigrationJobStatus.Running;
            job.ProcessedMessages = 0;
            job.TotalMessages = 0;
            job.SkippedMessages = 0;
            job.DuplicateMessages = 0;
            job.CurrentFolder = null;
            job.ErrorMessage = null;
            await _jobRepository.UpdateAsync(job);
            await _progressNotifier.SendStatusChangeAsync(project.Id, job.Id, job.Status.ToString(), null);

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

            // Count total messages across all mapped folders (with date filtering)
            var mappings = job.FolderMappings.OrderBy(m => m.SourceFolderName).ToList();
            var folderMessageUids = await GetFilteredMessageUids(job, project, mappings, cancellationToken);

            job.TotalMessages = folderMessageUids.Values.Sum(uids => uids.Count);
            await _jobRepository.UpdateAsync(job);
            await _progressNotifier.SendProgressAsync(project.Id, job.Id, 0, job.TotalMessages, null, 0, 0);

            _logger.LogInformation("Job {JobId}: {TotalMessages} messages across {FolderCount} folders (after date filtering)",
                jobId, job.TotalMessages, mappings.Count);

            if (job.TotalMessages == 0)
            {
                job.Status = MigrationJobStatus.Completed;
                job.CurrentFolder = null;
                await _jobRepository.UpdateAsync(job);
                await _progressNotifier.SendJobCompletedAsync(project.Id, job.Id, 0, 0, 0, 0, null);
                _logger.LogInformation("Job {JobId}: No messages to migrate, completed", jobId);
                return;
            }

            // Migrate each folder
            int skippedMessages = 0;
            int duplicateMessages = 0;
            int failedMessages = 0;

            foreach (var mapping in mappings)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var allUids = folderMessageUids.GetValueOrDefault(mapping.SourceFolderName);
                if (allUids is null || allUids.Count == 0)
                {
                    _logger.LogDebug("Job {JobId}: Folder '{Folder}' has no matching messages, skipping", jobId, mapping.SourceFolderName);
                    continue;
                }

                // Resume: skip UIDs already processed in a previous run
                IList<UniqueId> uids;
                if (isResume && mapping.LastProcessedUid.HasValue)
                {
                    var checkpoint = mapping.LastProcessedUid.Value;
                    uids = allUids.Where(u => u.Id > checkpoint).ToList();
                    var skippedCount = allUids.Count - uids.Count;
                    if (skippedCount > 0)
                    {
                        _logger.LogInformation("Job {JobId}: Folder '{Folder}' — skipping {Skipped} already-processed messages (checkpoint UID {Uid})",
                            jobId, mapping.SourceFolderName, skippedCount, checkpoint);
                        job.ProcessedMessages += skippedCount;
                    }
                }
                else
                {
                    uids = allUids;
                }

                if (uids.Count == 0)
                    continue;

                job.CurrentFolder = mapping.SourceFolderName;
                await _jobRepository.UpdateAsync(job);

                _logger.LogInformation("Job {JobId}: Migrating folder '{Folder}' → '{DestFolder}' ({Count} messages)",
                    jobId, mapping.SourceFolderName, mapping.DestinationFolderDisplayName, uids.Count);

                // Connect to IMAP and migrate messages
                using var imapClient = MigrationHelpers.CreateImapClient();
                try
                {
                    await MigrationHelpers.ConnectAndAuthenticate(imapClient, job, project, _encryptor, _oauthProvider, _logger, cancellationToken);

                    var folder = await imapClient.GetFolderAsync(mapping.SourceFolderName, cancellationToken);
                    await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                    bool isSentFolder = IsSentFolder(mapping.SourceFolderName);

                    foreach (var uid in uids)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            // Rate limiting to respect Graph API limits
                            await Task.Delay(RateLimitDelay, cancellationToken);

                            // Refresh token if needed (tokens expire after ~1 hour)
                            if (token.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
                            {
                                token = await credential.GetTokenAsync(tokenContext, cancellationToken);
                                httpClient.DefaultRequestHeaders.Authorization =
                                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
                            }

                            var message = await folder.GetMessageAsync(uid, cancellationToken);

                            // Post-fetch date filter for Sent Items (IMAP SEARCH uses InternalDate, but Sent Items should use Date header)
                            if (isSentFolder && !PassesDateFilter(message.Date, job.DateFrom, job.DateTo))
                            {
                                skippedMessages++;
                                logBuffer.Add(new MigrationLog
                                {
                                    MigrationJobId = job.Id,
                                    Type = MigrationLogType.Skipped,
                                    Subject = message.Subject,
                                    MessageDate = message.Date != DateTimeOffset.MinValue ? message.Date.UtcDateTime : null,
                                    SourceFolder = mapping.SourceFolderName,
                                    ErrorMessage = "Outside date filter range"
                                });
                                await _progressNotifier.SendLogEntryAsync(project.Id, job.Id, "Skipped", message.Subject, mapping.SourceFolderName, "Outside date filter range");
                                job.ProcessedMessages++;
                                job.SkippedMessages = skippedMessages;
                                await SendProgressAndPersist(job, project.Id, skippedMessages, logBuffer, mapping, uid);
                                continue;
                            }

                            // Duplicate check via Graph API
                            if (checkDuplicates && !string.IsNullOrEmpty(message.MessageId))
                            {
                                var isDuplicate = await CheckDuplicateAsync(httpClient, job.DestinationEmail,
                                    mapping.DestinationFolderId, message.MessageId, cancellationToken);
                                if (isDuplicate)
                                {
                                    _logger.LogDebug("Job {JobId}: Already present '{Subject}' (Message-ID: {MessageId})",
                                        jobId, message.Subject, message.MessageId);
                                    duplicateMessages++;
                                    job.ProcessedMessages++;
                                    job.DuplicateMessages = duplicateMessages;
                                    await SendProgressAndPersist(job, project.Id, skippedMessages, logBuffer, mapping, uid);
                                    continue;
                                }
                            }

                            using var mimeStream = new MemoryStream();
                            await message.WriteToAsync(mimeStream, cancellationToken);

                            if (mimeStream.Length > MigrationHelpers.MaxMimeSize)
                            {
                                var sizeMb = mimeStream.Length / (1024.0 * 1024.0);
                                _logger.LogWarning("Job {JobId}: Skipping message '{Subject}' in '{Folder}' — size {Size}MB exceeds 4MB limit",
                                    jobId, message.Subject, mapping.SourceFolderName, sizeMb);
                                skippedMessages++;
                                logBuffer.Add(new MigrationLog
                                {
                                    MigrationJobId = job.Id,
                                    Type = MigrationLogType.Skipped,
                                    Subject = message.Subject,
                                    MessageDate = message.Date != DateTimeOffset.MinValue ? message.Date.UtcDateTime : null,
                                    SourceFolder = mapping.SourceFolderName,
                                    ErrorMessage = $"Size {sizeMb:F1}MB exceeds 4MB limit"
                                });
                                await _progressNotifier.SendLogEntryAsync(project.Id, job.Id, "Skipped", message.Subject, mapping.SourceFolderName, $"Size {sizeMb:F1}MB exceeds 4MB limit");
                                job.ProcessedMessages++;
                                job.SkippedMessages = skippedMessages;
                                await SendProgressAndPersist(job, project.Id, skippedMessages, logBuffer, mapping, uid);
                                continue;
                            }

                            // Build Graph API JSON message with MAPI flags to prevent draft status
                            var graphMessage = MigrationHelpers.ConvertToGraphMessage(message);
                            var uploadUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(job.DestinationEmail)}/mailFolders/{mapping.DestinationFolderId}/messages";

                            _logger.LogDebug("Job {JobId}: Uploading '{Subject}' to '{DestFolder}'",
                                jobId, message.Subject, mapping.DestinationFolderDisplayName);

                            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(graphMessage);

                            var response = await UploadWithRetryAsync(httpClient, uploadUrl, jsonPayload, jobId, cancellationToken);

                            if (!response.IsSuccessStatusCode)
                            {
                                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                                var errorDetail = $"{response.StatusCode}: {responseBody[..Math.Min(500, responseBody.Length)]}";
                                _logger.LogWarning("Job {JobId}: Failed to upload message '{Subject}' to '{DestFolder}' — {Error}",
                                    jobId, message.Subject, mapping.DestinationFolderDisplayName, errorDetail);
                                failedMessages++;
                                logBuffer.Add(new MigrationLog
                                {
                                    MigrationJobId = job.Id,
                                    Type = MigrationLogType.Error,
                                    Subject = message.Subject,
                                    MessageDate = message.Date != DateTimeOffset.MinValue ? message.Date.UtcDateTime : null,
                                    SourceFolder = mapping.SourceFolderName,
                                    ErrorMessage = errorDetail.Length > 2000 ? errorDetail[..2000] : errorDetail,
                                    InternetMessageId = message.MessageId,
                                    SourceUid = uid.Id,
                                    DestinationFolderId = mapping.DestinationFolderId
                                });
                                await _progressNotifier.SendLogEntryAsync(project.Id, job.Id, "Error", message.Subject, mapping.SourceFolderName, response.StatusCode.ToString());
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Job {JobId}: Error processing message {Uid} in '{Folder}'",
                                jobId, uid, mapping.SourceFolderName);
                            failedMessages++;
                            var errorMsg = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                            logBuffer.Add(new MigrationLog
                            {
                                MigrationJobId = job.Id,
                                Type = MigrationLogType.Error,
                                SourceFolder = mapping.SourceFolderName,
                                ErrorMessage = errorMsg,
                                SourceUid = uid.Id,
                                DestinationFolderId = mapping.DestinationFolderId
                            });
                            await _progressNotifier.SendLogEntryAsync(project.Id, job.Id, "Error", null, mapping.SourceFolderName, errorMsg);
                        }

                        job.ProcessedMessages++;
                        job.SkippedMessages = skippedMessages;
                        await SendProgressAndPersist(job, project.Id, skippedMessages, logBuffer, mapping, uid);
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

            // Flush remaining log buffer
            if (logBuffer.Count > 0)
            {
                await _logRepository.CreateBatchAsync(logBuffer);
                logBuffer.Clear();
            }

            // Write summary log entry
            await _logRepository.CreateAsync(new MigrationLog
            {
                MigrationJobId = job.Id,
                Type = MigrationLogType.Summary,
                TotalProcessed = job.ProcessedMessages,
                TotalFailed = failedMessages,
                TotalSkipped = skippedMessages,
                TotalDuplicates = duplicateMessages
            });

            // Completed — clear checkpoints so next run starts fresh
            job.Status = MigrationJobStatus.Completed;
            job.CurrentFolder = null;
            job.SkippedMessages = skippedMessages;
            job.DuplicateMessages = duplicateMessages;
            foreach (var m in job.FolderMappings)
                m.LastProcessedUid = null;

            // Only failed and skipped (>4MB/date) count as warnings — duplicates are normal
            if (skippedMessages > 0 || failedMessages > 0)
            {
                job.ErrorMessage = $"Completed with {failedMessages} failed and {skippedMessages} skipped messages.";
            }

            await _jobRepository.UpdateAsync(job);
            await _progressNotifier.SendJobCompletedAsync(project.Id, job.Id,
                job.ProcessedMessages, job.TotalMessages, failedMessages, skippedMessages, job.ErrorMessage);
            _logger.LogInformation("Job {JobId}: Migration completed. {Processed}/{Total} messages. {Failed} failed, {Skipped} skipped, {Duplicates} already present.",
                jobId, job.ProcessedMessages, job.TotalMessages, failedMessages, skippedMessages, duplicateMessages);
        }
        catch (OperationCanceledException)
        {
            // Flush remaining log buffer before updating status
            try { if (logBuffer.Count > 0) await _logRepository.CreateBatchAsync(logBuffer); } catch { /* best effort */ }

            job.Status = MigrationJobStatus.Cancelled;
            job.CurrentFolder = null;
            await _jobRepository.UpdateAsync(job);
            await _progressNotifier.SendStatusChangeAsync(project.Id, job.Id, job.Status.ToString(), null);
            _logger.LogInformation("Job {JobId}: Migration cancelled at {Processed}/{Total} messages",
                jobId, job.ProcessedMessages, job.TotalMessages);
        }
        catch (Exception ex)
        {
            // Flush remaining log buffer before updating status
            try { if (logBuffer.Count > 0) await _logRepository.CreateBatchAsync(logBuffer); } catch { /* best effort */ }

            _logger.LogError(ex, "Job {JobId}: Migration failed", jobId);
            await SetJobFailed(job, ex.Message);
            await _progressNotifier.SendStatusChangeAsync(project.Id, job.Id, MigrationJobStatus.Failed.ToString(), ex.Message);
        }
    }

    private async Task<HttpResponseMessage> UploadWithRetryAsync(
        HttpClient httpClient, string uploadUrl, string jsonPayload, Guid jobId,
        CancellationToken ct, int maxAttempts = 3)
    {
        HttpResponseMessage? response = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            response = await httpClient.PostAsync(uploadUrl, content, ct);

            if (response.IsSuccessStatusCode)
                return response;

            if (!IsTransientError(response) || attempt == maxAttempts)
                return response;

            // Exponential backoff: for 429 use RetryAfter header, otherwise 1s, 2s, 4s
            var delay = response.StatusCode == HttpStatusCode.TooManyRequests
                ? (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)))
                : TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

            _logger.LogWarning("Job {JobId}: Transient error {Status}, attempt {Attempt}/{Max}, waiting {Delay}s",
                jobId, response.StatusCode, attempt, maxAttempts, delay.TotalSeconds);
            await Task.Delay(delay, ct);
        }
        return response!;
    }

    private static bool IsTransientError(HttpResponseMessage response)
    {
        return response.StatusCode is HttpStatusCode.TooManyRequests       // 429
            or HttpStatusCode.ServiceUnavailable                           // 503
            or HttpStatusCode.GatewayTimeout                               // 504
            or HttpStatusCode.RequestTimeout;                              // 408
    }

    private async Task SendProgressAndPersist(MigrationJob job, Guid projectId, int skipped,
        List<MigrationLog> logBuffer, FolderMapping? mapping = null, UniqueId? currentUid = null)
    {
        await _progressNotifier.SendProgressAsync(projectId, job.Id,
            job.ProcessedMessages, job.TotalMessages, job.CurrentFolder, skipped, job.DuplicateMessages);

        // Persist to DB periodically to reduce DB load
        if (job.ProcessedMessages % DbPersistInterval == 0)
        {
            // Update checkpoint UID for resume support
            if (mapping is not null && currentUid.HasValue)
                mapping.LastProcessedUid = currentUid.Value.Id;

            await _jobRepository.UpdateAsync(job);
            if (logBuffer.Count > 0)
            {
                await _logRepository.CreateBatchAsync(logBuffer);
                logBuffer.Clear();
            }
        }
    }

    private static bool IsSentFolder(string folderName)
    {
        return folderName.Contains("Sent", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PassesDateFilter(DateTimeOffset messageDate, DateTime? dateFrom, DateTime? dateTo)
    {
        if (messageDate == DateTimeOffset.MinValue)
            return true; // No date available, don't filter

        var date = messageDate.UtcDateTime;
        if (dateFrom.HasValue && date < dateFrom.Value.Date)
            return false;
        if (dateTo.HasValue && date > dateTo.Value.Date.AddDays(1).AddTicks(-1))
            return false;
        return true;
    }

    private async Task<bool> CheckDuplicateAsync(HttpClient httpClient, string destEmail,
        string destFolderId, string messageId, CancellationToken ct)
    {
        try
        {
            // Escape the Message-ID for OData filter (encode angle brackets and single quotes)
            var escapedId = messageId.Replace("'", "''");
            var filterUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(destEmail)}" +
                            $"/mailFolders/{destFolderId}/messages" +
                            $"?$filter=internetMessageId eq '{Uri.EscapeDataString(escapedId)}'&$select=id&$top=1";

            var response = await httpClient.GetAsync(filterUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Duplicate check failed for Message-ID {MessageId}: {Status}", messageId, response.StatusCode);
                return false; // On error, assume not duplicate (upload anyway)
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            // Check if the response contains any results
            return json.Contains("\"id\"", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Duplicate check error for Message-ID {MessageId}", messageId);
            return false; // On error, assume not duplicate
        }
    }


    private string? ValidatePreconditions(MigrationJob job, Project project)
    {
        if (!job.MigrationOptionsConfigured)
            return "Migration options not configured.";

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

    /// <summary>
    /// Gets UIDs of messages per folder, applying date range filtering via IMAP SEARCH
    /// to avoid downloading messages that will be skipped.
    /// </summary>
    private async Task<Dictionary<string, IList<UniqueId>>> GetFilteredMessageUids(
        MigrationJob job, Project project, List<FolderMapping> mappings, CancellationToken ct)
    {
        var result = new Dictionary<string, IList<UniqueId>>();

        using var client = MigrationHelpers.CreateImapClient();
        await MigrationHelpers.ConnectAndAuthenticate(client, job, project, _encryptor, _oauthProvider, _logger, ct);

        foreach (var mapping in mappings)
        {
            try
            {
                var folder = await client.GetFolderAsync(mapping.SourceFolderName, ct);
                await folder.OpenAsync(FolderAccess.ReadOnly, ct);

                // Build IMAP SEARCH query with date range (uses InternalDate)
                var searchQuery = BuildDateSearchQuery(job.DateFrom, job.DateTo);

                IList<UniqueId> uids;
                if (searchQuery is not null)
                {
                    uids = await folder.SearchAsync(searchQuery, ct);
                }
                else
                {
                    // No date filter: get all UIDs
                    uids = await folder.SearchAsync(MailKit.Search.SearchQuery.All, ct);
                }

                result[mapping.SourceFolderName] = uids;
                await folder.CloseAsync(false, ct);

                _logger.LogDebug("Folder '{Folder}': {Count} messages match date filter", mapping.SourceFolderName, uids.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not search messages in folder '{Folder}'", mapping.SourceFolderName);
                result[mapping.SourceFolderName] = Array.Empty<UniqueId>();
            }
        }

        await client.DisconnectAsync(true, ct);
        return result;
    }

    private static MailKit.Search.SearchQuery? BuildDateSearchQuery(DateTime? dateFrom, DateTime? dateTo)
    {
        MailKit.Search.SearchQuery? query = null;

        if (dateFrom.HasValue)
        {
            query = MailKit.Search.SearchQuery.DeliveredAfter(dateFrom.Value.Date.AddDays(-1));
        }

        if (dateTo.HasValue)
        {
            var beforeQuery = MailKit.Search.SearchQuery.DeliveredBefore(dateTo.Value.Date.AddDays(1));
            query = query is not null ? query.And(beforeQuery) : beforeQuery;
        }

        return query;
    }

    private async Task SetJobFailed(MigrationJob job, string errorMessage)
    {
        job.Status = MigrationJobStatus.Failed;
        job.ErrorMessage = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;
        job.CurrentFolder = null;
        await _jobRepository.UpdateAsync(job);
        _logger.LogError("Job {JobId}: Failed — {Error}", job.Id, errorMessage);
    }

}
