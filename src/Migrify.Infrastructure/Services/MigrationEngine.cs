using System.Diagnostics;
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
    private readonly IMigrationQueueService _queueService;
    private readonly ILogger<MigrationEngine> _logger;

    private const int DbPersistInterval = 10; // Persist to DB every N messages
    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromMilliseconds(150); // ~7 msg/sec with duplicate check

    public MigrationEngine(
        IMigrationJobRepository jobRepository,
        IMigrationLogRepository logRepository,
        ICredentialEncryptor encryptor,
        IImapOAuthCredentialProvider oauthProvider,
        IMigrationProgressNotifier progressNotifier,
        IMigrationQueueService queueService,
        ILogger<MigrationEngine> logger)
    {
        _jobRepository = jobRepository;
        _logRepository = logRepository;
        _encryptor = encryptor;
        _oauthProvider = oauthProvider;
        _progressNotifier = progressNotifier;
        _queueService = queueService;
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

            // Effective date range: for incremental re-runs, auto-fill DateFrom with LastCompletedAt
            var effectiveDateFrom = job.DateFrom;
            var effectiveDateTo = job.DateTo;
            if (job.MigrationMode == MigrationMode.Incremental && job.LastCompletedAt.HasValue && !effectiveDateFrom.HasValue)
            {
                effectiveDateFrom = job.LastCompletedAt.Value;
                _logger.LogInformation("Job {JobId}: Incremental mode — using LastCompletedAt {Date:u} as DateFrom",
                    jobId, effectiveDateFrom);
            }

            // Resume detection: check if folder mappings have checkpoint UIDs
            bool isResume = job.FolderMappings.Any(m => m.LastProcessedUid.HasValue);
            Dictionary<string, HashSet<uint>> retryUids = new();
            if (isResume)
            {
                // Load UIDs of previously failed/skipped messages so they get re-processed on resume
                retryUids = await _logRepository.GetFailedAndSkippedUidsByJobIdAsync(job.Id);
                _logger.LogInformation("Job {JobId}: Resuming from checkpoint — skipping successfully-processed UIDs per folder", jobId);
            }

            // Set status to Running, record start time, and reset counters
            job.Status = MigrationJobStatus.Running;
            job.StartedAt = DateTime.UtcNow;
            job.CompletedAt = null;
            job.ProcessedMessages = 0;
            job.TotalMessages = 0;
            job.SkippedMessages = 0;
            job.DuplicateMessages = 0;
            job.CurrentFolder = null;
            job.ErrorMessage = null;
            await _jobRepository.UpdateAsync(job);
            await _progressNotifier.SendStatusChangeAsync(project.Id, job.Id, job.Status.ToString(), null);

            var jobSw = Stopwatch.StartNew();

            // Decrypt M365 credentials
            var m365 = project.M365Settings!;
            var clientSecret = _encryptor.Decrypt(m365.EncryptedClientSecret!);
            var credential = new ClientSecretCredential(m365.TenantId, m365.ClientId, clientSecret);

            // Get bearer token for Graph API
            _logger.LogInformation("Job {JobId}: Acquiring M365 token for tenant {TenantId}", jobId, m365.TenantId);
            var tokenSw = Stopwatch.StartNew();
            var tokenContext = new Azure.Core.TokenRequestContext(["https://graph.microsoft.com/.default"]);
            var token = await credential.GetTokenAsync(tokenContext, cancellationToken);
            tokenSw.Stop();
            _logger.LogInformation("Job {JobId}: M365 token acquired in {ElapsedMs}ms (expires {ExpiresOn})",
                jobId, tokenSw.ElapsedMilliseconds, token.ExpiresOn);

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            // Count total messages across all mapped folders (with date filtering)
            var mappings = job.FolderMappings.OrderBy(m => m.SourceFolderName).ToList();
            var folderMessageUids = await GetFilteredMessageUids(job, project, mappings, effectiveDateFrom, effectiveDateTo, cancellationToken);

            job.TotalMessages = folderMessageUids.Values.Sum(uids => uids.Count);
            await _jobRepository.UpdateAsync(job);
            await _progressNotifier.SendProgressAsync(project.Id, job.Id, 0, job.TotalMessages, null, 0, 0);

            _logger.LogInformation("Job {JobId}: {TotalMessages} messages across {FolderCount} folders (after date filtering)",
                jobId, job.TotalMessages, mappings.Count);

            // DB info log: job started with config summary
            var modeLabel = job.MigrationMode == MigrationMode.Incremental ? "Incremental" : "Full Copy";
            var dateRange = (effectiveDateFrom.HasValue || effectiveDateTo.HasValue)
                ? $", date range: {effectiveDateFrom?.ToString("yyyy-MM-dd") ?? "∞"} → {effectiveDateTo?.ToString("yyyy-MM-dd") ?? "∞"}"
                : "";
            var resumeLabel = isResume ? ", resuming from checkpoint" : "";
            await _logRepository.CreateAsync(new MigrationLog
            {
                MigrationJobId = job.Id,
                Type = MigrationLogType.Info,
                ErrorMessage = $"Job started: {modeLabel}, {job.TotalMessages} messages in {mappings.Count} folders{dateRange}{resumeLabel}. " +
                    $"Source: {job.SourceEmail}, Destination: {job.DestinationEmail}. " +
                    $"Duplicates: {(checkDuplicates ? "skip" : "allow")}."
            });

            if (job.TotalMessages == 0)
            {
                job.Status = MigrationJobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;
                job.LastCompletedAt = job.CompletedAt;
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

            // Single IMAP connection for the entire job — avoids repeated SSL fallback per folder
            using var imapClient = MigrationHelpers.CreateImapClient();
            var imapConnSw = Stopwatch.StartNew();
            await MigrationHelpers.ConnectAndAuthenticate(imapClient, job, project, _encryptor, _oauthProvider, _logger, cancellationToken);
            imapConnSw.Stop();

            // DB info log: connection established
            var sourceDesc = project.SourceConnectorType == SourceConnectorType.GoogleWorkspace && !job.HasImapOverride
                ? $"Google Workspace (imap.gmail.com:993, service account: {project.GoogleWorkspaceSettings!.ServiceAccountEmail})"
                : $"IMAP ({job.ImapSettings!.Host}:{job.ImapSettings.Port}, {job.ImapSettings.Encryption}, auth: {job.ImapSettings.AuthType})";
            await _logRepository.CreateAsync(new MigrationLog
            {
                MigrationJobId = job.Id,
                Type = MigrationLogType.Info,
                ErrorMessage = $"Connections established — IMAP: {sourceDesc} in {imapConnSw.ElapsedMilliseconds}ms. " +
                    $"M365: tenant {m365.TenantId} token in {tokenSw.ElapsedMilliseconds}ms."
            });

            foreach (var mapping in mappings)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var allUids = folderMessageUids.GetValueOrDefault(mapping.SourceFolderName);
                if (allUids is null || allUids.Count == 0)
                {
                    _logger.LogDebug("Job {JobId}: Folder '{Folder}' has no matching messages, skipping", jobId, mapping.SourceFolderName);
                    continue;
                }

                // Resume: skip only successfully-processed UIDs; re-process failed/skipped ones
                IList<UniqueId> uids;
                if (isResume && mapping.LastProcessedUid.HasValue)
                {
                    var checkpoint = mapping.LastProcessedUid.Value;
                    var folderRetryUids = retryUids.GetValueOrDefault(mapping.SourceFolderName);
                    uids = allUids.Where(u => u.Id > checkpoint
                        || (folderRetryUids is not null && folderRetryUids.Contains(u.Id))).ToList();
                    var skippedCount = allUids.Count - uids.Count;
                    if (skippedCount > 0)
                    {
                        _logger.LogInformation("Job {JobId}: Folder '{Folder}' — skipping {Skipped} successfully-processed messages (checkpoint UID {Uid})",
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

                // Reconnect if connection was lost between folders
                if (!imapClient.IsConnected)
                {
                    _logger.LogWarning("Job {JobId}: IMAP connection lost before folder '{Folder}', reconnecting",
                        jobId, mapping.SourceFolderName);
                    var reconnSw = Stopwatch.StartNew();
                    await MigrationHelpers.ConnectAndAuthenticate(imapClient, job, project, _encryptor, _oauthProvider, _logger, cancellationToken);
                    reconnSw.Stop();
                    _logger.LogInformation("Job {JobId}: IMAP reconnected in {ElapsedMs}ms", jobId, reconnSw.ElapsedMilliseconds);
                    await _logRepository.CreateAsync(new MigrationLog
                    {
                        MigrationJobId = job.Id,
                        Type = MigrationLogType.Info,
                        SourceFolder = mapping.SourceFolderName,
                        ErrorMessage = $"IMAP connection lost and reconnected in {reconnSw.ElapsedMilliseconds}ms"
                    });
                }

                try
                {
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
                            if (isSentFolder && !PassesDateFilter(message.Date, effectiveDateFrom, effectiveDateTo))
                            {
                                skippedMessages++;
                                logBuffer.Add(new MigrationLog
                                {
                                    MigrationJobId = job.Id,
                                    Type = MigrationLogType.Skipped,
                                    Subject = message.Subject,
                                    MessageDate = message.Date != DateTimeOffset.MinValue ? message.Date.UtcDateTime : null,
                                    SourceFolder = mapping.SourceFolderName,
                                    ErrorMessage = "Outside date filter range",
                                    SourceUid = uid.Id
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
                                    ErrorMessage = $"Size {sizeMb:F1}MB exceeds 4MB limit",
                                    SourceUid = uid.Id
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
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Job {JobId}: IMAP error for folder '{Folder}' — {ErrorType}: {ErrorMessage}",
                        jobId, mapping.SourceFolderName, ex.GetType().Name, ex.Message);
                    throw;
                }
            }

            // Disconnect IMAP after all folders are done
            if (imapClient.IsConnected)
                await imapClient.DisconnectAsync(true, cancellationToken);

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

            // Completed — preserve checkpoints for incremental re-runs
            jobSw.Stop();
            job.Status = MigrationJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.LastCompletedAt = job.CompletedAt;
            job.CurrentFolder = null;
            job.SkippedMessages = skippedMessages;
            job.DuplicateMessages = duplicateMessages;

            // Only failed and skipped (>4MB/date) count as warnings — duplicates are normal
            if (skippedMessages > 0 || failedMessages > 0)
            {
                job.ErrorMessage = $"Completed with {failedMessages} failed and {skippedMessages} skipped messages.";
            }

            await _jobRepository.UpdateAsync(job);
            await _progressNotifier.SendJobCompletedAsync(project.Id, job.Id,
                job.ProcessedMessages, job.TotalMessages, failedMessages, skippedMessages, job.ErrorMessage);

            // DB info log: job completed with duration and throughput
            var duration = jobSw.Elapsed;
            var durationStr = duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s"
                : duration.TotalMinutes >= 1
                    ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
                    : $"{duration.Seconds}s";
            var throughput = duration.TotalMinutes > 0 ? job.ProcessedMessages / duration.TotalMinutes : job.ProcessedMessages;
            await _logRepository.CreateAsync(new MigrationLog
            {
                MigrationJobId = job.Id,
                Type = MigrationLogType.Info,
                ErrorMessage = $"Job completed in {durationStr}. " +
                    $"{job.ProcessedMessages}/{job.TotalMessages} messages processed ({throughput:F0} msg/min). " +
                    $"{failedMessages} failed, {skippedMessages} skipped, {duplicateMessages} duplicates."
            });

            _logger.LogInformation("Job {JobId}: Migration completed in {Duration}. {Processed}/{Total} messages ({Throughput:F0} msg/min). {Failed} failed, {Skipped} skipped, {Duplicates} already present.",
                jobId, durationStr, job.ProcessedMessages, job.TotalMessages, throughput, failedMessages, skippedMessages, duplicateMessages);
        }
        catch (OperationCanceledException)
        {
            // Flush remaining log buffer before updating status
            try { if (logBuffer.Count > 0) await _logRepository.CreateBatchAsync(logBuffer); } catch { /* best effort */ }

            // Determine cancellation reason
            var cancelReason = _queueService.WasUserCancelled(jobId)
                ? "Cancelled by user"
                : "Cancelled due to application shutdown";

            job.Status = MigrationJobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            job.CurrentFolder = null;
            job.ErrorMessage = cancelReason;
            await _jobRepository.UpdateAsync(job);
            await _progressNotifier.SendStatusChangeAsync(project.Id, job.Id, job.Status.ToString(), cancelReason);

            // DB info log: cancellation with reason and progress
            try
            {
                await _logRepository.CreateAsync(new MigrationLog
                {
                    MigrationJobId = job.Id,
                    Type = MigrationLogType.Info,
                    ErrorMessage = $"{cancelReason} at {job.ProcessedMessages}/{job.TotalMessages} messages. " +
                        $"Last folder: {job.CurrentFolder ?? "(none)"}."
                });
            }
            catch { /* best effort */ }

            _logger.LogWarning("Job {JobId}: {CancelReason} at {Processed}/{Total} messages",
                jobId, cancelReason, job.ProcessedMessages, job.TotalMessages);
        }
        catch (Exception ex)
        {
            // Flush remaining log buffer before updating status
            try { if (logBuffer.Count > 0) await _logRepository.CreateBatchAsync(logBuffer); } catch { /* best effort */ }

            _logger.LogError(ex, "Job {JobId}: Migration failed — {ErrorType}: {ErrorMessage}",
                jobId, ex.GetType().Name, ex.Message);
            job.CompletedAt = DateTime.UtcNow;
            await SetJobFailed(job, ex.Message);
            await _progressNotifier.SendStatusChangeAsync(project.Id, job.Id, MigrationJobStatus.Failed.ToString(), ex.Message);

            // DB info log: failure with full error context
            try
            {
                var innerMsg = ex.InnerException is not null ? $" Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "";
                var errorContext = $"Job failed at {job.ProcessedMessages}/{job.TotalMessages} messages. " +
                    $"Last folder: {job.CurrentFolder ?? "(none)"}. " +
                    $"{ex.GetType().Name}: {ex.Message}{innerMsg}";
                if (errorContext.Length > 2000) errorContext = errorContext[..2000];
                await _logRepository.CreateAsync(new MigrationLog
                {
                    MigrationJobId = job.Id,
                    Type = MigrationLogType.Info,
                    ErrorMessage = errorContext
                });
            }
            catch { /* best effort */ }
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

        // Always update checkpoint in memory (so completion save has the final UID)
        if (mapping is not null && currentUid.HasValue)
            mapping.LastProcessedUid = currentUid.Value.Id;

        // Persist to DB periodically to reduce DB load
        if (job.ProcessedMessages % DbPersistInterval == 0)
        {
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
        MigrationJob job, Project project, List<FolderMapping> mappings,
        DateTime? dateFrom, DateTime? dateTo, CancellationToken ct)
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
                var searchQuery = BuildDateSearchQuery(dateFrom, dateTo);

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
