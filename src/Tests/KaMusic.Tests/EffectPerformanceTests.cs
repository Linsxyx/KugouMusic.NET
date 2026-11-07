namespace AvaloniaSilkEffects.Tests;

public sealed class EffectPerformanceTests
{
    private static EffectFrameTimings Sample(double value) => new(value, value, value, value, value, value, value, (long)value);

    [Fact]
    public void PercentilesUseNearestRankAndIncludeColdFrames()
    {
        var history = new EffectPerformanceHistory(100);
        for (var i = 1; i <= 100; i++) history.Record(Sample(i), i == 1 ? 0 : 16);
        var snapshot = history.Snapshot();
        Assert.Equal(100, snapshot.SampleCount);
        Assert.Equal(new(50.5, 95, 99, 100), snapshot.Cpu);
        Assert.Equal(snapshot.Cpu, snapshot.Update);
        Assert.Equal(snapshot.Cpu, snapshot.AllocatedBytes);
        Assert.Equal(new(16, 16, 16, 16), snapshot.FrameInterval);
    }

    [Fact]
    public void WindowDropsOldSamplesAndResetDropsPreviousSession()
    {
        var history = new EffectPerformanceHistory(3);
        history.Record(Sample(999));
        history.Record(Sample(1)); history.Record(Sample(2)); history.Record(Sample(3));
        Assert.Equal(new(2, 3, 3, 3), history.Snapshot().Cpu);
        history.Reset();
        Assert.Equal(default, history.Snapshot());
        history.Record(Sample(7));
        Assert.Equal(new(7, 7, 7, 7), history.Snapshot().Cpu);
    }

    [Fact]
    public void RecordingAndReadingDoNotAllocateAfterWarmup()
    {
        var history = new EffectPerformanceHistory();
        for (var i = 0; i < 1000; i++) history.Record(Sample(i));
        history.Snapshot();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) history.Record(Sample(i));
        history.Snapshot();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void NestedUiWorkIsCountedOnceAndAllocationsAreObserved()
    {
        var meter = new EffectCpuWorkMeter();
        using (meter.Measure())
        {
            using (meter.Measure()) GC.KeepAlive(new byte[4096]);
        }
        var sample = meter.History.Snapshot();
        Assert.Equal(1, sample.SampleCount);
        Assert.True(sample.AllocatedBytes.Mean >= 4096);
    }

    [Fact]
    public void GpuLedgerHandlesResizeReleaseAndContextLossWithoutDoubleCounting()
    {
        var ledger = new EffectGpuResources();
        ledger.Texture(1, 64); ledger.Target(1, 128); ledger.Buffer(1, 256);
        Assert.Equal(448, ledger.Snapshot.EstimatedBytes);
        ledger.Target(1, 512);
        Assert.Equal(1, ledger.Snapshot.RenderTargets);
        Assert.Equal(832, ledger.Snapshot.EstimatedBytes);
        ledger.ReleaseTexture(1); ledger.ReleaseTexture(1);
        Assert.Equal(1, ledger.FrameReleasedTextures);
        ledger.BeginFrame();
        Assert.Equal(0, ledger.FrameReleasedTextures);
        Assert.Equal(768, ledger.Snapshot.EstimatedBytes);
        ledger.Abandon();
        Assert.Equal(default, ledger.Snapshot);
    }

    [Fact]
    public void TextSubStagesAreReportedWithoutAddingThemToFrameCpu()
    {
        var history = new EffectPerformanceHistory();
        history.Record(Sample(10) with { TextWork = new(1, 2, 3, 4, 1, 1024) });
        var snapshot = history.Snapshot();
        Assert.Equal(10, snapshot.Cpu.Mean);
        Assert.Equal(1, snapshot.TextMeasure.Mean);
        Assert.Equal(2, snapshot.TextRaster.Mean);
        Assert.Equal(3, snapshot.TextUpload.Mean);
        Assert.Equal(4, snapshot.TextWarmup.Mean);
    }

    [Fact]
    public void MipStorageIncludesEveryLevelOfNonSquareTextures()
    {
        Assert.Equal(4 * (8 * 4 + 4 * 2 + 2 * 1 + 1), EffectGpuResources.RgbaBytes(8, 4, true));
        Assert.Equal(8 * 4 * 4, EffectGpuResources.RgbaBytes(8, 4));
    }
}
