using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Wander.Harness.Host;

/// <summary>
/// Process and GC numbers, sampled once a second from a background thread
/// and on demand at <c>measure</c> steps. Written out as metrics.json and
/// summarised in the report: peak and final working set, collections per
/// generation, bytes allocated, handles and threads, CPU share.
/// </summary>
public sealed class MetricsSampler : IDisposable {
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly List<Sample> _samples = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Timer _timer;
    private TimeSpan _lastCpu;
    private long _lastCpuAtMs;


    public MetricsSampler() {
        _lastCpu = _process.TotalProcessorTime;
        _timer = new Timer(_ => Take(null), null, 1000, 1000);
    }


    public IReadOnlyList<Sample> Samples {
        get {
            lock (_samples) {
                return _samples.ToList();
            }
        }
    }


    public Sample Take(string? label) {
        _process.Refresh();
        long now = _clock.ElapsedMilliseconds;
        var cpu = _process.TotalProcessorTime;
        double cpuShare = 0;
        long wall = now - _lastCpuAtMs;
        if (wall > 0) {
            cpuShare = (cpu - _lastCpu).TotalMilliseconds / wall / Environment.ProcessorCount * 100;
        }
        _lastCpu = cpu;
        _lastCpuAtMs = now;

        var gc = GC.GetGCMemoryInfo();
        var sample = new Sample(
            now,
            label,
            _process.WorkingSet64,
            _process.PrivateMemorySize64,
            GC.GetTotalAllocatedBytes(),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            gc.HeapSizeBytes,
            gc.GenerationInfo.Length > 3 ? gc.GenerationInfo[3].SizeAfterBytes : 0,
            gc.PauseTimePercentage,
            _process.HandleCount,
            _process.Threads.Count,
            Math.Round(cpuShare, 1));
        lock (_samples) {
            _samples.Add(sample);
        }

        return sample;
    }

    public void WriteJson(string path) {
        var json = JsonSerializer.Serialize(Samples, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public string Summary() {
        var all = Samples;
        if (all.Count == 0) {
            return "no samples";
        }

        var first = all[0];
        var last = all[^1];
        long peakWs = all.Max(s => s.WorkingSet);
        long peakPrivate = all.Max(s => s.PrivateBytes);
        double avgCpu = all.Skip(1).Select(s => s.CpuPercent).DefaultIfEmpty(0).Average();

        return
            $"working set: peak {Mb(peakWs)} MB, final {Mb(last.WorkingSet)} MB; private: peak {Mb(peakPrivate)} MB\n" +
            $"GC: gen0 {last.Gen0 - first.Gen0}, gen1 {last.Gen1 - first.Gen1}, gen2 {last.Gen2 - first.Gen2}; " +
            $"allocated {Mb(last.AllocatedBytes - first.AllocatedBytes)} MB; heap {Mb(last.HeapBytes)} MB, LOH {Mb(last.LohBytes)} MB; pause {last.GcPausePercent:F1} %\n" +
            $"handles: {all.Min(s => s.Handles)}..{all.Max(s => s.Handles)}; threads: {all.Min(s => s.Threads)}..{all.Max(s => s.Threads)}; " +
            $"cpu avg {avgCpu:F1} % over {all.Count} samples";
    }

    public void Dispose() {
        _timer.Dispose();
        _process.Dispose();
    }


    private static long Mb(long bytes) {
        return bytes / (1024 * 1024);
    }
}


public sealed record Sample(
    long AtMs,
    string? Label,
    long WorkingSet,
    long PrivateBytes,
    long AllocatedBytes,
    int Gen0,
    int Gen1,
    int Gen2,
    long HeapBytes,
    long LohBytes,
    double GcPausePercent,
    int Handles,
    int Threads,
    double CpuPercent);
