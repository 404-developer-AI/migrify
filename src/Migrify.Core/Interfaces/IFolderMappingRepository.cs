using Migrify.Core.Entities;

namespace Migrify.Core.Interfaces;

public interface IFolderMappingRepository
{
    Task<List<FolderMapping>> GetByJobIdAsync(Guid migrationJobId);
    Task SaveMappingsAsync(Guid migrationJobId, List<FolderMapping> mappings);
}
