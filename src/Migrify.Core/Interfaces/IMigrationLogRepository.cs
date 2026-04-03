using Migrify.Core.Entities;

namespace Migrify.Core.Interfaces;

public interface IMigrationLogRepository
{
    Task CreateAsync(MigrationLog log);
    Task CreateBatchAsync(IEnumerable<MigrationLog> logs);
    Task<(List<MigrationLog> Items, int TotalCount)> GetByJobIdAsync(
        Guid jobId, int page, int pageSize,
        MigrationLogType? typeFilter = null, string? searchText = null);
    Task<(List<MigrationLog> Items, int TotalCount)> GetByProjectIdAsync(
        Guid projectId, int page, int pageSize,
        MigrationLogType? typeFilter = null, string? searchText = null);
    Task<Dictionary<MigrationLogType, int>> GetCountsByJobIdAsync(Guid jobId);
    Task<Dictionary<MigrationLogType, int>> GetCountsByProjectIdAsync(Guid projectId);
    Task<MigrationLog?> GetByIdAsync(Guid logId);
    Task UpdateAsync(MigrationLog log);
    Task<List<MigrationLog>> GetUnretriedErrorsByJobIdAsync(Guid jobId);
}
