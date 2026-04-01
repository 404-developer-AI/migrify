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
            .Include(j => j.ImapSettings)
            .Where(j => j.ProjectId == projectId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();
    }

    public async Task<MigrationJob?> GetByIdAsync(Guid id)
    {
        return await _db.MigrationJobs
            .Include(j => j.ImapSettings)
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
        _db.MigrationJobs.Update(job);
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
}
