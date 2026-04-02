using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Migrify.Core.Interfaces;
using Migrify.Web.Hubs;

namespace Migrify.Web.Services;

public class SignalRMigrationProgressNotifier : IMigrationProgressNotifier
{
    private readonly IHubContext<MigrationProgressHub> _hubContext;
    private readonly ConcurrentDictionary<Guid, DateTime> _lastProgressSent = new();
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMilliseconds(500);

    public SignalRMigrationProgressNotifier(IHubContext<MigrationProgressHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendProgressAsync(Guid projectId, Guid jobId, int processed, int total, string? currentFolder, int skipped, int duplicates)
    {
        var now = DateTime.UtcNow;
        var lastSent = _lastProgressSent.GetOrAdd(jobId, DateTime.MinValue);

        if (now - lastSent < ThrottleInterval)
            return;

        _lastProgressSent[jobId] = now;

        await _hubContext.Clients.Group($"project-{projectId}")
            .SendAsync("ReceiveProgress", jobId, processed, total, currentFolder, skipped, duplicates);
    }

    public async Task SendStatusChangeAsync(Guid projectId, Guid jobId, string status, string? errorMessage)
    {
        await _hubContext.Clients.Group($"project-{projectId}")
            .SendAsync("ReceiveStatusChange", jobId, status, errorMessage);
    }

    public async Task SendJobCompletedAsync(Guid projectId, Guid jobId, int processed, int total, int failed, int skipped, string? errorMessage)
    {
        // Remove throttle tracking for completed job
        _lastProgressSent.TryRemove(jobId, out _);

        await _hubContext.Clients.Group($"project-{projectId}")
            .SendAsync("ReceiveJobCompleted", jobId, processed, total, failed, skipped, errorMessage);
    }
}
