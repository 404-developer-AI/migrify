namespace Migrify.Core.Entities;

public class ImapProviderPreset
{
    public Guid Id { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public ProviderMatchType MatchType { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 993;
    public ImapEncryption Encryption { get; set; } = ImapEncryption.SSL;
    public string? ProviderName { get; set; }
}
