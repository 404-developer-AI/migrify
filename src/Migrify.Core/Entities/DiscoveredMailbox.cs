namespace Migrify.Core.Entities;

public class DiscoveredMailbox
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public MailboxSide Side { get; set; }
    public DateTime DiscoveredAtUtc { get; set; }

    public Project Project { get; set; } = null!;
}
