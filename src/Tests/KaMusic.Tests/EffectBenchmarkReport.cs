using System.Text.Json;
using Avalonia;

namespace AvaloniaSilkEffects.Tests;

internal sealed class EffectBenchmarkReport
{
    private readonly List<Sample> _samples = [];
    private sealed record Sample(double SongSeconds, EffectDeviceFrameMetrics Metrics, double? SynchronizedMilliseconds);

    public void Add(double seconds, EffectDevice device, double? synchronizedMilliseconds = null) =>
        _samples.Add(new(seconds, device.FrameMetrics, synchronizedMilliseconds));

    public void Write(string theme, EffectDevice device, PixelSize size, double scaling)
    {
        var history = new EffectPerformanceHistory(Math.Max(1, _samples.Count));
        var synchronized = new EffectPerformanceHistory(Math.Max(1, _samples.Count));
        foreach (var sample in _samples)
        {
            history.Record(sample.Metrics.Timings);
            if (sample.SynchronizedMilliseconds is { } ms)
                synchronized.Record(new(0, 0, 0, 0, 0, 0, ms, 0));
        }
        var directory = Environment.GetEnvironmentVariable("EFFECT_BENCHMARK_DIR") ??
            Path.Combine(Path.GetTempPath(), "kugou-render-bench", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, theme + ".json");
        var summary = history.Snapshot();
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, Theme = theme, CapturedUtc = DateTime.UtcNow,
            BuildConfiguration = typeof(EffectBenchmarkReport).Assembly.GetCustomAttributes(false)
                .OfType<System.Reflection.AssemblyConfigurationAttribute>().FirstOrDefault()?.Configuration,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            device.Renderer, device.OpenGlVersion, PixelWidth = size.Width, PixelHeight = size.Height, Scaling = scaling,
            Scope = "Offscreen scene only; CPU timings exclude initialization/Resize and driver execution. Text preparation is nested. Allocations are calling-thread managed bytes. GPU storage is estimated. Synchronized timings include glFinish only when supplied; no compositor or shared background.",
            Cpu = summary, SynchronizedCpuAndGpu = synchronized.Snapshot().SampleCount == 0 ? (EffectMetricDistribution?)null : synchronized.Snapshot().Cpu,
            FirstFrame = _samples.FirstOrDefault(),
            SlowestCpuFrames = _samples.OrderByDescending(s => s.Metrics.Timings.CpuMilliseconds).Take(10),
            Samples = _samples,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{theme} CPU mean/P95/P99/max: {summary.Cpu.Mean:F2}/{summary.Cpu.P95:F2}/{summary.Cpu.P99:F2}/{summary.Cpu.Maximum:F2} ms; report: {path}");
    }
}
