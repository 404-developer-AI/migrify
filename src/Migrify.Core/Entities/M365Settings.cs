namespace Migrify.Core.Entities;

public class M365Settings
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? EncryptedClientSecret { get; set; }

    public Project Project { get; set; } = null!;
}
