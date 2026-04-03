namespace Migrify.Core.Entities;

public class AppSettings
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public static class AppSettingKeys
{
    public const string ConcurrencyOverrideMax = "ConcurrencyOverride:Max";
    public const string ConcurrencyOverridePerTenant = "ConcurrencyOverride:PerTenant";
    public const string ConcurrencyOverridePerSource = "ConcurrencyOverride:PerSource";
}
