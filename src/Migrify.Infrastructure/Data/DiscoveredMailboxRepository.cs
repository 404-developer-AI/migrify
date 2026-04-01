using Microsoft.EntityFrameworkCore;
using Migrify.Core.Entities;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Data;

public class DiscoveredMailboxRepository : IDiscoveredMailboxRepository
{
    private readonly ApplicationDbContext _db;

    public DiscoveredMailboxRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<DiscoveredMailbox>> GetByProjectAndSideAsync(Guid projectId, MailboxSide side)
    {
        return await _db.DiscoveredMailboxes
            .Where(m => m.ProjectId == projectId && m.Side == side)
            .OrderBy(m => m.Email)
            .ToListAsync();
    }

    public async Task ReplaceAllAsync(Guid projectId, MailboxSide side, List<DiscoveredMailbox> mailboxes)
    {
        var existing = await _db.DiscoveredMailboxes
            .Where(m => m.ProjectId == projectId && m.Side == side)
            .ToListAsync();

        _db.DiscoveredMailboxes.RemoveRange(existing);

        foreach (var mailbox in mailboxes)
        {
            mailbox.Id = Guid.NewGuid();
            mailbox.ProjectId = projectId;
            mailbox.Side = side;
            mailbox.DiscoveredAtUtc = DateTime.UtcNow;
        }

        _db.DiscoveredMailboxes.AddRange(mailboxes);
        await _db.SaveChangesAsync();
    }
}
