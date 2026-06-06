using System.Diagnostics;

namespace NexStrap.Services;

public record PerformanceStats(double CpuPercent, long MemoryMb);

public class PerformanceMonitorService : IDisposable
{
    private Timer? _timer;
    private TimeSpan _lastCpuTime;
    private DateTime _lastMeasure = DateTime.UtcNow;

    public event EventHandler<PerformanceStats>? StatsUpdated;

    public void Start()
    {
        if (_timer != null) return;
        _timer = new Timer(_ => Measure(), null, 1000, 1000);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Measure()
    {
        try
        {
            var proc = Process.GetProcessesByName("RobloxPlayerBeta").FirstOrDefault()
                    ?? Process.GetProcessesByName("RobloxPlayer").FirstOrDefault();
            if (proc == null || proc.HasExited) return;

            var now = DateTime.UtcNow;
            var cpu = proc.TotalProcessorTime;
            var elapsed = (now - _lastMeasure).TotalSeconds;

            double cpuPercent = 0;
            if (elapsed > 0)
                cpuPercent = Math.Min(100, (cpu - _lastCpuTime).TotalSeconds / elapsed / Environment.ProcessorCount * 100);

            _lastCpuTime = cpu;
            _lastMeasure = now;

            var memMb = proc.WorkingSet64 / 1024 / 1024;
            StatsUpdated?.Invoke(this, new PerformanceStats(cpuPercent, memMb));
        }
        catch { }
    }

    public void Dispose() => Stop();
}
