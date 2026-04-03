using Microsoft.EntityFrameworkCore;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Data;

public class MigrationLogRepository : IMigrationLogRepository
{
    private readonly ApplicationDbContext _db;

    public MigrationLogRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task CreateAsync(MigrationLog log)
    {
        log.Id = Guid.NewGuid();
        log.CreatedAt = DateTime.UtcNow;
        _db.MigrationLogs.Add(log);
        await _db.SaveChangesAsync();
    }

    public async Task CreateBatchAsync(IEnumerable<MigrationLog> logs)
    {
        var logList = logs.ToList();
        if (logList.Count == 0) return;

        foreach (var log in logList)
        {
            log.Id = Guid.NewGuid();
            log.CreatedAt = DateTime.UtcNow;
        }
        _db.MigrationLogs.AddRange(logList);
        await _db.SaveChangesAsync();
    }

    public async Task<(List<MigrationLog> Items, int TotalCount)> GetByJobIdAsync(
        Guid jobId, int page, int pageSize,
        MigrationLogType? typeFilter = null, string? searchText = null)
    {
        var query = _db.MigrationLogs.AsNoTracking()
            .Where(l => l.MigrationJobId == jobId);

        query = ApplyFilters(query, typeFilter, searchText);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<(List<MigrationLog> Items, int TotalCount)> GetByProjectIdAsync(
        Guid projectId, int page, int pageSize,
        MigrationLogType? typeFilter = null, string? searchText = null)
    {
        var query = _db.MigrationLogs.AsNoTracking()
            .Include(l => l.MigrationJob)
            .Where(l => l.MigrationJob.ProjectId == projectId);

        query = ApplyFilters(query, typeFilter, searchText);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Dictionary<MigrationLogType, int>> GetCountsByJobIdAsync(Guid jobId)
    {
        return await _db.MigrationLogs.AsNoTracking()
            .Where(l => l.MigrationJobId == jobId)
            .GroupBy(l => l.Type)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task<Dictionary<MigrationLogType, int>> GetCountsByProjectIdAsync(Guid projectId)
    {
        return await _db.MigrationLogs.AsNoTracking()
            .Where(l => l.MigrationJob.ProjectId == projectId)
            .GroupBy(l => l.Type)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task<MigrationLog?> GetByIdAsync(Guid logId)
    {
        return await _db.MigrationLogs.FindAsync(logId);
    }

    public async Task UpdateAsync(MigrationLog log)
    {
        _db.MigrationLogs.Update(log);
        await _db.SaveChangesAsync();
    }

    public async Task<List<MigrationLog>> GetUnretriedErrorsByJobIdAsync(Guid jobId)
    {
        return await _db.MigrationLogs
            .Where(l => l.MigrationJobId == jobId && l.Type == MigrationLogType.Error)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();
    }

    private static IQueryable<MigrationLog> ApplyFilters(
        IQueryable<MigrationLog> query,
        MigrationLogType? typeFilter, string? searchText)
    {
        if (typeFilter.HasValue)
            query = query.Where(l => l.Type == typeFilter.Value);

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var pattern = $"%{searchText}%";
            query = query.Where(l =>
                EF.Functions.ILike(l.Subject ?? "", pattern) ||
                EF.Functions.ILike(l.ErrorMessage ?? "", pattern));
        }

        return query;
    }
}
