namespace Migrify.Core.Interfaces;

public interface IAppSettingsService
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task DeleteAsync(string key);
    Task<Dictionary<string, string>> GetAllAsync();
    int? GetConcurrencyOverrideMax();
    int? GetConcurrencyOverridePerTenant();
    int? GetConcurrencyOverridePerSource();
}
