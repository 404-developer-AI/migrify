using System.Collections.Concurrent;
using System.Threading.Channels;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Services;

public class MigrationQueueService : IMigrationQueueService
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningJobs = new();

    public async ValueTask EnqueueAsync(Guid jobId)
    {
        await _queue.Writer.WriteAsync(jobId);
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }

    public bool TryCancel(Guid jobId)
    {
        if (_runningJobs.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }

    public bool IsRunning(Guid jobId)
    {
        return _runningJobs.ContainsKey(jobId);
    }

    public CancellationTokenSource RegisterCancellation(Guid jobId)
    {
        var cts = new CancellationTokenSource();
        _runningJobs[jobId] = cts;
        return cts;
    }

    public void RemoveJob(Guid jobId)
    {
        if (_runningJobs.TryRemove(jobId, out var cts))
        {
            cts.Dispose();
        }
    }
}
