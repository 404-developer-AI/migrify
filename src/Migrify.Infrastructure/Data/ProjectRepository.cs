using Microsoft.EntityFrameworkCore;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Data;

public class ProjectRepository : IProjectRepository
{
    private readonly ApplicationDbContext _db;

    public ProjectRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Project>> GetAllAsync()
    {
        return await _db.Projects
            .AsNoTracking()
            .Include(p => p.MigrationJobs)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<Project?> GetByIdAsync(Guid id)
    {
        return await _db.Projects
            .AsNoTracking()
            .Include(p => p.MigrationJobs)
            .Include(p => p.M365Settings)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Project?> GetByIdWithConnectorsAsync(Guid id)
    {
        return await _db.Projects
            .AsNoTracking()
            .Include(p => p.MigrationJobs)
                .ThenInclude(j => j.ImapSettings)
            .Include(p => p.M365Settings)
            .Include(p => p.GoogleWorkspaceSettings)
            .Include(p => p.DiscoveredMailboxes)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Project> CreateAsync(Project project)
    {
        project.Id = Guid.NewGuid();
        project.CreatedAt = DateTime.UtcNow;
        project.UpdatedAt = DateTime.UtcNow;

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    public async Task UpdateAsync(Project project)
    {
        project.UpdatedAt = DateTime.UtcNow;

        var entry = _db.Entry(project);
        if (entry.State == EntityState.Detached)
        {
            _db.Projects.Update(project);
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project is not null)
        {
            _db.Projects.Remove(project);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<int> GetCountAsync()
    {
        return await _db.Projects.CountAsync();
    }
}
