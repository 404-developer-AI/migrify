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
            .Include(p => p.ImapSettings)
            .Include(p => p.M365Settings)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<Project?> GetByIdAsync(Guid id)
    {
        return await _db.Projects
            .Include(p => p.ImapSettings)
            .Include(p => p.M365Settings)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Project> CreateAsync(Project project)
    {
        project.Id = Guid.NewGuid();
        project.CreatedAt = DateTime.UtcNow;
        project.UpdatedAt = DateTime.UtcNow;

        if (project.ImapSettings is not null)
            project.ImapSettings.Id = Guid.NewGuid();

        if (project.M365Settings is not null)
            project.M365Settings.Id = Guid.NewGuid();

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    public async Task UpdateAsync(Project project)
    {
        project.UpdatedAt = DateTime.UtcNow;
        _db.Projects.Update(project);
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
