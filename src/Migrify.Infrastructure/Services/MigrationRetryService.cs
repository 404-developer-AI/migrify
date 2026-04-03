using System.Text;
using Azure.Identity;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Services;

public class MigrationRetryService : IMigrationRetryService
{
    private readonly IMigrationLogRepository _logRepository;
    private readonly IMigrationJobRepository _jobRepository;
    private readonly ICredentialEncryptor _encryptor;
    private readonly IImapOAuthCredentialProvider _oauthProvider;
    private readonly IMigrationProgressNotifier _progressNotifier;
    private readonly ILogger<MigrationRetryService> _logger;

    public MigrationRetryService(
        IMigrationLogRepository logRepository,
        IMigrationJobRepository jobRepository,
        ICredentialEncryptor encryptor,
        IImapOAuthCredentialProvider oauthProvider,
        IMigrationProgressNotifier progressNotifier,
        ILogger<MigrationRetryService> logger)
    {
        _logRepository = logRepository;
        _jobRepository = jobRepository;
        _encryptor = encryptor;
        _oauthProvider = oauthProvider;
        _progressNotifier = progressNotifier;
        _logger = logger;
    }

    public async Task<bool> RetryAsync(Guid logId, CancellationToken cancellationToken = default)
    {
        var log = await _logRepository.GetByIdAsync(logId);
        if (log is null)
        {
            _logger.LogWarning("Retry: Log entry {LogId} not found", logId);
            return false;
        }

        if (log.Type != MigrationLogType.Error)
        {
            _logger.LogWarning("Retry: Log entry {LogId} is not an error (type: {Type})", logId, log.Type);
            return false;
        }

        if (log.SourceUid is null || string.IsNullOrEmpty(log.SourceFolder) || string.IsNullOrEmpty(log.DestinationFolderId))
        {
            _logger.LogWarning("Retry: Log entry {LogId} missing retry data (SourceUid/SourceFolder/DestinationFolderId)", logId);
            return false;
        }

        var job = await _jobRepository.GetByIdWithProjectAsync(log.MigrationJobId);
        if (job is null)
        {
            _logger.LogWarning("Retry: Job {JobId} not found for log {LogId}", log.MigrationJobId, logId);
            return false;
        }

        var project = job.Project;

        try
        {
            // Connect to IMAP and fetch the message
            using var imapClient = MigrationHelpers.CreateImapClient();
            await MigrationHelpers.ConnectAndAuthenticate(imapClient, job, project, _encryptor, _oauthProvider, _logger, cancellationToken);

            var folder = await imapClient.GetFolderAsync(log.SourceFolder, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uid = new UniqueId(log.SourceUid.Value);
            var message = await folder.GetMessageAsync(uid, cancellationToken);

            await folder.CloseAsync(false, cancellationToken);
            await imapClient.DisconnectAsync(true, cancellationToken);

            // Check MIME size
            using var mimeStream = new MemoryStream();
            await message.WriteToAsync(mimeStream, cancellationToken);

            if (mimeStream.Length > MigrationHelpers.MaxMimeSize)
            {
                var sizeMb = mimeStream.Length / (1024.0 * 1024.0);
                log.ErrorMessage = $"Retry failed: Size {sizeMb:F1}MB exceeds 4MB limit";
                await _logRepository.UpdateAsync(log);
                return false;
            }

            // Upload to Graph API
            var m365 = project.M365Settings!;
            var clientSecret = _encryptor.Decrypt(m365.EncryptedClientSecret!);
            var credential = new ClientSecretCredential(m365.TenantId, m365.ClientId, clientSecret);
            var tokenContext = new Azure.Core.TokenRequestContext(["https://graph.microsoft.com/.default"]);
            var token = await credential.GetTokenAsync(tokenContext, cancellationToken);

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            var graphMessage = MigrationHelpers.ConvertToGraphMessage(message);
            var uploadUrl = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(job.DestinationEmail)}/mailFolders/{log.DestinationFolderId}/messages";
            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(graphMessage);

            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(uploadUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Retry: Successfully retried log {LogId} (subject: {Subject})", logId, log.Subject);
                log.Type = MigrationLogType.Retried;
                await _logRepository.UpdateAsync(log);
                await _progressNotifier.SendRetryResultAsync(project.Id, job.Id, logId, true);
                return true;
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var errorDetail = $"Retry failed: {response.StatusCode}: {responseBody[..Math.Min(500, responseBody.Length)]}";
                _logger.LogWarning("Retry: Failed for log {LogId} — {Error}", logId, errorDetail);
                log.ErrorMessage = errorDetail.Length > 2000 ? errorDetail[..2000] : errorDetail;
                await _logRepository.UpdateAsync(log);
                await _progressNotifier.SendRetryResultAsync(project.Id, job.Id, logId, false);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retry: Exception for log {LogId}", logId);
            log.ErrorMessage = $"Retry failed: {(ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message)}";
            await _logRepository.UpdateAsync(log);
            await _progressNotifier.SendRetryResultAsync(project.Id, job.Id, logId, false);
            return false;
        }
    }

    public async Task<(int Succeeded, int Failed)> RetryAllFailedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var errorLogs = await _logRepository.GetUnretriedErrorsByJobIdAsync(jobId);
        int succeeded = 0;
        int failed = 0;

        _logger.LogInformation("Retry all: {Count} failed messages for job {JobId}", errorLogs.Count, jobId);

        foreach (var log in errorLogs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await RetryAsync(log.Id, cancellationToken))
                succeeded++;
            else
                failed++;
        }

        _logger.LogInformation("Retry all: Job {JobId} — {Succeeded} succeeded, {Failed} failed", jobId, succeeded, failed);
        return (succeeded, failed);
    }
}
