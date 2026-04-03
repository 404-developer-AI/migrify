namespace Migrify.Infrastructure.Services;

public class SystemResourceMonitor
{
    private const long ReservedMemoryBytes = 512 * 1024 * 1024L; // 512 MB for OS + app
    private const long MemoryPerJobBytes = 250 * 1024 * 1024L;   // 250 MB per job
    private const int JobsPerCpu = 3;                              // I/O-bound workload

    private int _cachedLimit;
    private DateTime _lastCalculated = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(60);
    private readonly object _lock = new();

    public int GetSystemLimit()
    {
        lock (_lock)
        {
            if (DateTime.UtcNow - _lastCalculated < _cacheDuration && _cachedLimit > 0)
                return _cachedLimit;

            var memoryLimit = GetMemoryBasedLimit();
            var cpuLimit = GetCpuBasedLimit();
            _cachedLimit = Math.Min(memoryLimit, cpuLimit);
            _lastCalculated = DateTime.UtcNow;
            return _cachedLimit;
        }
    }

    public void Refresh()
    {
        lock (_lock)
        {
            _lastCalculated = DateTime.MinValue;
        }
    }

    public long GetTotalAvailableMemoryBytes()
    {
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    public int GetProcessorCount()
    {
        return Environment.ProcessorCount;
    }

    private int GetMemoryBasedLimit()
    {
        var totalMemory = GetTotalAvailableMemoryBytes();
        var availableForJobs = totalMemory - ReservedMemoryBytes;
        if (availableForJobs <= 0)
            return 1;

        return Math.Max(1, (int)(availableForJobs / MemoryPerJobBytes));
    }

    private int GetCpuBasedLimit()
    {
        return Math.Max(1, GetProcessorCount() * JobsPerCpu);
    }
}
