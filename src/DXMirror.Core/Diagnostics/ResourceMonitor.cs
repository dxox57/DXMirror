using System.Diagnostics;
using System.Net.NetworkInformation;

namespace DXMirror.Core.Diagnostics;

public sealed record ResourceSample(
    DateTimeOffset Time,
    double AppCpuPercent,
    long AppWorkingSetBytes,
    double ReceiverCpuPercent,
    long ReceiverWorkingSetBytes,
    double NetworkReceiveKbps,
    int ManagedThreads);

/// <summary>
/// Samples CPU and RAM of DXMirror and of the receiver process, plus network receive
/// throughput of the selected adapter. Cheap (one sample per second, no WMI / perf counters).
/// GPU load is not sampled (would need the Windows "GPU Engine" performance counters).
/// </summary>
public sealed class ResourceMonitor
{
    private readonly struct CpuState(TimeSpan cpu, long ticks)
    {
        public TimeSpan Cpu { get; } = cpu;
        public long Ticks { get; } = ticks;
    }

    private CpuState? _app;
    private CpuState? _receiver;
    private int? _receiverPid;
    private long? _lastNetBytes;
    private long _lastNetTicks;

    public ResourceSample Sample(int? receiverPid, string? adapterId)
    {
        var now = Stopwatch.GetTimestamp();
        double appCpu;
        long appWs;
        using (var self = Process.GetCurrentProcess())
        {
            appCpu = CpuPercent(ref _app, self.TotalProcessorTime, now);
            appWs = self.WorkingSet64;
        }

        double recCpu = 0;
        long recWs = 0;
        if (receiverPid != _receiverPid) { _receiver = null; _receiverPid = receiverPid; }
        if (receiverPid is { } pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                recCpu = CpuPercent(ref _receiver, p.TotalProcessorTime, now);
                recWs = p.WorkingSet64;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _receiver = null;
            }
        }

        double netKbps = 0;
        if (adapterId is not null)
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == adapterId);
                if (nic is not null)
                {
                    var bytes = nic.GetIPStatistics().BytesReceived;
                    if (_lastNetBytes is { } last && now > _lastNetTicks)
                    {
                        var seconds = (now - _lastNetTicks) / (double)Stopwatch.Frequency;
                        netKbps = Math.Max(0, (bytes - last) * 8 / 1000.0 / seconds);
                    }
                    _lastNetBytes = bytes;
                    _lastNetTicks = now;
                }
            }
            catch (NetworkInformationException) { }
        }

        return new ResourceSample(DateTimeOffset.Now, appCpu, appWs, recCpu, recWs, netKbps, ThreadPool.ThreadCount);
    }

    private static double CpuPercent(ref CpuState? state, TimeSpan cpu, long now)
    {
        double result = 0;
        if (state is { } prev && now > prev.Ticks)
        {
            var wall = (now - prev.Ticks) / (double)Stopwatch.Frequency;
            result = (cpu - prev.Cpu).TotalSeconds / wall / Environment.ProcessorCount * 100.0;
        }
        state = new CpuState(cpu, now);
        return Math.Clamp(result, 0, 100);
    }
}
