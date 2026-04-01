using Migrify.Core.Entities;

namespace Migrify.Core.Interfaces;

public interface IDiscoveredMailboxRepository
{
    Task<List<DiscoveredMailbox>> GetByProjectAndSideAsync(Guid projectId, MailboxSide side);
    Task ReplaceAllAsync(Guid projectId, MailboxSide side, List<DiscoveredMailbox> mailboxes);
}
