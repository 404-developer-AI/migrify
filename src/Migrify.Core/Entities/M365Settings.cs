namespace Migrify.Core.Entities;

public class M365Settings
{
    public Guid Id { get; set; }
    public Guid MigrationJobId { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? EncryptedClientSecret { get; set; }

    public MigrationJob MigrationJob { get; set; } = null!;
}
