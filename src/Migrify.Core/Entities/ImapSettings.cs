namespace Migrify.Core.Entities;

public class ImapSettings
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 993;
    public ImapEncryption Encryption { get; set; } = ImapEncryption.SSL;
    public ImapAuthType AuthType { get; set; } = ImapAuthType.Password;
    public string? Username { get; set; }
    public string? EncryptedPassword { get; set; }
    public string? LastTestedServerAddress { get; set; }
    public string? ResolvedIpAddress { get; set; }

    // OAuth2 per-project credentials (e.g. Google Cloud Console Client ID/Secret)
    public string? OAuthClientId { get; set; }
    public string? EncryptedOAuthClientSecret { get; set; }

    // OAuth2 tokens (stored encrypted)
    public string? EncryptedOAuthAccessToken { get; set; }
    public string? EncryptedOAuthRefreshToken { get; set; }
    public DateTime? OAuthTokenExpiresAtUtc { get; set; }

    // OAuth2 provider identifier (e.g. "Google")
    public string? OAuthProvider { get; set; }

    public Project Project { get; set; } = null!;
}
