using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Data;

public class ProjectRepository : IProjectRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ProjectRepository> _logger;

    public ProjectRepository(ApplicationDbContext db, ILogger<ProjectRepository> logger)
    {
        _db = db;
        _logger = logger;
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

        // Blazor Server shares a scoped DbContext across the circuit lifetime.
        // Clear tracked entities to prevent conflicts when attaching an untracked instance.
        _db.ChangeTracker.Clear();

        // Attach the project as Modified (existing entity)
        _db.Projects.Update(project);

        // Fix entity states: Update() marks everything as Modified, but new child entities
        // need to be Added (they don't exist in the database yet).
        if (project.M365Settings is not null)
        {
            var m365Entry = _db.Entry(project.M365Settings);
            if (project.M365Settings.Id == Guid.Empty)
            {
                project.M365Settings.Id = Guid.NewGuid();
                project.M365Settings.ProjectId = project.Id;
                m365Entry.State = EntityState.Added;
            }
        }

        if (project.GoogleWorkspaceSettings is not null)
        {
            var gwsEntry = _db.Entry(project.GoogleWorkspaceSettings);
            if (project.GoogleWorkspaceSettings.Id == Guid.Empty)
            {
                project.GoogleWorkspaceSettings.Id = Guid.NewGuid();
                project.GoogleWorkspaceSettings.ProjectId = project.Id;
                gwsEntry.State = EntityState.Added;
            }
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
