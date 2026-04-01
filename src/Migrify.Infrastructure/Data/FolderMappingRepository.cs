using Microsoft.EntityFrameworkCore;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Data;

public class FolderMappingRepository : IFolderMappingRepository
{
    private readonly ApplicationDbContext _db;

    public FolderMappingRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<FolderMapping>> GetByJobIdAsync(Guid migrationJobId)
    {
        return await _db.FolderMappings
            .Where(m => m.MigrationJobId == migrationJobId)
            .OrderBy(m => m.SourceFolderName)
            .ToListAsync();
    }

    public async Task SaveMappingsAsync(Guid migrationJobId, List<FolderMapping> mappings)
    {
        var existing = await _db.FolderMappings
            .Where(m => m.MigrationJobId == migrationJobId)
            .ToListAsync();

        _db.FolderMappings.RemoveRange(existing);

        foreach (var mapping in mappings)
        {
            mapping.Id = Guid.NewGuid();
            mapping.MigrationJobId = migrationJobId;
            mapping.CreatedAt = DateTime.UtcNow;
        }

        _db.FolderMappings.AddRange(mappings);
        await _db.SaveChangesAsync();
    }
}
