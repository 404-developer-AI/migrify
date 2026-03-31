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

    public Project Project { get; set; } = null!;
}
