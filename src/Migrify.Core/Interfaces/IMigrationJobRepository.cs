using Migrify.Core.Entities;

namespace Migrify.Core.Interfaces;

public interface IMigrationJobRepository
{
    Task<List<MigrationJob>> GetByProjectIdAsync(Guid projectId);
    Task<MigrationJob?> GetByIdAsync(Guid id);
    Task<MigrationJob?> GetByIdWithProjectAsync(Guid id);
    Task<MigrationJob> CreateAsync(MigrationJob job);
    Task UpdateAsync(MigrationJob job);
    Task DeleteAsync(Guid id);
    Task<int> GetCountAsync();
    Task<Dictionary<MigrationJobStatus, int>> GetStatusCountsAsync();
    Task<List<MigrationJob>> GetAllWithProjectAsync(int? limit = null);
}
