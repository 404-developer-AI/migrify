using System.Collections.Concurrent;
using System.Threading.Channels;
using Migrify.Core.Interfaces;

namespace Migrify.Infrastructure.Services;

public class MigrationQueueService : IMigrationQueueService
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runningJobs = new();
    private readonly ConcurrentDictionary<Guid, bool> _queuedJobs = new();
    private readonly ConcurrentDictionary<Guid, bool> _cancelledFromQueue = new();

    public async ValueTask EnqueueAsync(Guid jobId)
    {
        _queuedJobs[jobId] = true;
        await _queue.Writer.WriteAsync(jobId);
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
    {
        var jobId = await _queue.Reader.ReadAsync(cancellationToken);
        _queuedJobs.TryRemove(jobId, out _);
        return jobId;
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

    public bool IsQueued(Guid jobId)
    {
        return _queuedJobs.ContainsKey(jobId);
    }

    public bool TryCancelQueued(Guid jobId)
    {
        if (_queuedJobs.TryRemove(jobId, out _))
        {
            _cancelledFromQueue[jobId] = true;
            return true;
        }
        return false;
    }

    public bool WasCancelledFromQueue(Guid jobId)
    {
        return _cancelledFromQueue.TryRemove(jobId, out _);
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
