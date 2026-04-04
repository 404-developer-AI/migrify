using Microsoft.EntityFrameworkCore;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Data;

public class MigrationJobRepository : IMigrationJobRepository
{
    private readonly ApplicationDbContext _db;

    public MigrationJobRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<MigrationJob>> GetByProjectIdAsync(Guid projectId)
    {
        return await _db.MigrationJobs
            .AsNoTracking()
            .Include(j => j.ImapSettings)
            .Include(j => j.FolderMappings)
            .Where(j => j.ProjectId == projectId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    public async Task<MigrationJob?> GetByIdAsync(Guid id)
    {
        return await _db.MigrationJobs
            .Include(j => j.ImapSettings)
            .Include(j => j.FolderMappings)
            .FirstOrDefaultAsync(j => j.Id == id);
    }

    public async Task<MigrationJob?> GetByIdWithProjectAsync(Guid id)
    {
        return await _db.MigrationJobs
            .Include(j => j.ImapSettings)
            .Include(j => j.FolderMappings)
            .Include(j => j.Project)
                .ThenInclude(p => p.M365Settings)
            .Include(j => j.Project)
                .ThenInclude(p => p.GoogleWorkspaceSettings)
            .FirstOrDefaultAsync(j => j.Id == id);
    }

    public async Task<MigrationJob> CreateAsync(MigrationJob job)
    {
        job.Id = Guid.NewGuid();
        job.CreatedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;

        if (job.ImapSettings is not null)
            job.ImapSettings.Id = Guid.NewGuid();

        _db.MigrationJobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    public async Task UpdateAsync(MigrationJob job)
    {
        job.UpdatedAt = DateTime.UtcNow;

        var entry = _db.Entry(job);
        if (entry.State == EntityState.Detached)
        {
            // Detach any already-tracked instances to avoid conflicts
            var tracked = _db.ChangeTracker.Entries<MigrationJob>()
                .FirstOrDefault(e => e.Entity.Id == job.Id);
            if (tracked is not null)
                tracked.State = EntityState.Detached;

            foreach (var trackedMapping in _db.ChangeTracker.Entries<FolderMapping>()
                .Where(e => e.Entity.MigrationJobId == job.Id).ToList())
                trackedMapping.State = EntityState.Detached;

            foreach (var trackedImap in _db.ChangeTracker.Entries<ImapSettings>()
                .Where(e => e.Entity.MigrationJobId == job.Id).ToList())
                trackedImap.State = EntityState.Detached;

            _db.MigrationJobs.Update(job);
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var job = await _db.MigrationJobs.FindAsync(id);
        if (job is not null)
        {
            _db.MigrationJobs.Remove(job);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<int> GetCountAsync()
    {
        return await _db.MigrationJobs.CountAsync();
    }

    public async Task<Dictionary<MigrationJobStatus, int>> GetStatusCountsAsync()
    {
        return await _db.MigrationJobs
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);
    }

    public async Task<List<MigrationJob>> GetAllWithProjectAsync(int? limit = null)
    {
        var query = _db.MigrationJobs
            .AsNoTracking()
            .Include(j => j.Project)
            .OrderBy(j => j.Status == MigrationJobStatus.Running ? 0 :
                          j.Status == MigrationJobStatus.Queued ? 1 :
                          j.Status == MigrationJobStatus.Failed ? 2 : 3)
            .ThenByDescending(j => j.UpdatedAt);

        if (limit.HasValue)
            return await query.Take(limit.Value).ToListAsync();

        return await query.ToListAsync();
    }
}
