using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;
using Migrify.Infrastructure.Data;

namespace Migrify.Infrastructure.Services;

public class AppSettingsService : IAppSettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private bool _loaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public AppSettingsService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        await _loadLock.WaitAsync();
        try
        {
            if (_loaded) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var settings = await db.AppSettings.AsNoTracking().ToListAsync();

            foreach (var setting in settings)
            {
                _cache[setting.Key] = setting.Value;
            }

            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<string?> GetAsync(string key)
    {
        await EnsureLoadedAsync();
        return _cache.TryGetValue(key, out var value) ? value : null;
    }

    public async Task SetAsync(string key, string value)
    {
        await EnsureLoadedAsync();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existing = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is not null)
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.AppSettings.Add(new AppSettings
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        _cache[key] = value;
    }

    public async Task DeleteAsync(string key)
    {
        await EnsureLoadedAsync();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existing = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is not null)
        {
            db.AppSettings.Remove(existing);
            await db.SaveChangesAsync();
        }

        _cache.TryRemove(key, out _);
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        await EnsureLoadedAsync();
        return new Dictionary<string, string>(_cache);
    }

    public int? GetConcurrencyOverrideMax()
    {
        return _cache.TryGetValue(AppSettingKeys.ConcurrencyOverrideMax, out var val)
            && int.TryParse(val, out var result) ? result : null;
    }

    public int? GetConcurrencyOverridePerTenant()
    {
        return _cache.TryGetValue(AppSettingKeys.ConcurrencyOverridePerTenant, out var val)
            && int.TryParse(val, out var result) ? result : null;
    }

    public int? GetConcurrencyOverridePerSource()
    {
        return _cache.TryGetValue(AppSettingKeys.ConcurrencyOverridePerSource, out var val)
            && int.TryParse(val, out var result) ? result : null;
    }
}
