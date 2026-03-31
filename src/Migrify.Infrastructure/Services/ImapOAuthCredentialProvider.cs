using Microsoft.Extensions.Logging;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Services;

public class ImapOAuthCredentialProvider : IImapOAuthCredentialProvider
{
    private readonly IOAuthTokenService _tokenService;
    private readonly IProjectRepository _projectRepository;
    private readonly ICredentialEncryptor _encryptor;
    private readonly ILogger<ImapOAuthCredentialProvider> _logger;

    public ImapOAuthCredentialProvider(
        IOAuthTokenService tokenService,
        IProjectRepository projectRepository,
        ICredentialEncryptor encryptor,
        ILogger<ImapOAuthCredentialProvider> logger)
    {
        _tokenService = tokenService;
        _projectRepository = projectRepository;
        _encryptor = encryptor;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(ImapSettings settings)
    {
        if (string.IsNullOrEmpty(settings.EncryptedOAuthRefreshToken))
            throw new InvalidOperationException("No OAuth2 tokens found. Please authorize first.");

        if (string.IsNullOrEmpty(settings.EncryptedOAuthAccessToken))
            throw new InvalidOperationException("No OAuth2 access token found. Please re-authorize.");

        // Check if access token is still valid (with 60s margin)
        if (settings.OAuthTokenExpiresAtUtc.HasValue &&
            settings.OAuthTokenExpiresAtUtc.Value > DateTime.UtcNow.AddSeconds(60))
        {
            _logger.LogDebug("Access token still valid until {Expiry}", settings.OAuthTokenExpiresAtUtc.Value);
            return _encryptor.Decrypt(settings.EncryptedOAuthAccessToken);
        }

        // Token expired or about to expire — refresh
        _logger.LogDebug("Access token expired or expiring soon, refreshing...");

        var refreshToken = _encryptor.Decrypt(settings.EncryptedOAuthRefreshToken);
        var clientId = settings.OAuthClientId
            ?? throw new InvalidOperationException("OAuth2 Client ID not configured.");
        var clientSecret = _encryptor.Decrypt(settings.EncryptedOAuthClientSecret
            ?? throw new InvalidOperationException("OAuth2 Client Secret not configured."));

        try
        {
            var result = await _tokenService.RefreshTokenAsync(refreshToken, clientId, clientSecret);

            // Persist new tokens
            settings.EncryptedOAuthAccessToken = _encryptor.Encrypt(result.AccessToken);
            settings.EncryptedOAuthRefreshToken = _encryptor.Encrypt(result.RefreshToken);
            settings.OAuthTokenExpiresAtUtc = result.ExpiresAtUtc;

            var project = await _projectRepository.GetByIdAsync(settings.ProjectId);
            if (project is not null)
                await _projectRepository.UpdateAsync(project);

            _logger.LogDebug("Token refreshed successfully, new expiry: {Expiry}", result.ExpiresAtUtc);

            return result.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed for project {ProjectId}", settings.ProjectId);
            throw new InvalidOperationException(
                "OAuth2 token refresh failed. Please re-authorize your Google account in project settings.", ex);
        }
    }
}
