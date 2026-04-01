namespace Migrify.Core.Entities;

public class GoogleWorkspaceSettings
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string ServiceAccountEmail { get; set; } = string.Empty;
    public string? EncryptedPrivateKey { get; set; }
    public string TokenUri { get; set; } = "https://oauth2.googleapis.com/token";
    public string ImpersonationEmail { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;

    public Project Project { get; set; } = null!;
}
