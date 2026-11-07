namespace AvaloniaSilkEffects.Tests;

public sealed class FrameRequestSchedulingTests
{
    [Fact]
    public void SchedulingSubtractsRenderTimeInsteadOfWaitingAnotherWholeInterval()
    {
        var pacer = new EffectFramePacer();
        pacer.ShouldPresent(TimeSpan.Zero, 60);

        var renderFinished = TimeSpan.FromMilliseconds(6);
        Assert.Equal(TimeSpan.FromSeconds(1d / 60) - renderFinished,
            pacer.GetNextFrameDelay(renderFinished, 60));
        Assert.Equal(TimeSpan.Zero, pacer.GetNextFrameDelay(TimeSpan.FromMilliseconds(20), 60));
    }

    [Fact]
    public void EarlyExplicitRedrawDoesNotPushBackContinuousAnimationDeadline()
    {
        var pacer = new EffectFramePacer();
        pacer.ShouldPresent(TimeSpan.Zero, 60);
        // Seek/resize callbacks must still draw, even before the next capped frame.
        pacer.ShouldPresent(TimeSpan.FromMilliseconds(8), 60);

        Assert.Equal(TimeSpan.FromSeconds(1d / 60) - TimeSpan.FromMilliseconds(10),
            pacer.GetNextFrameDelay(TimeSpan.FromMilliseconds(10), 60));
    }

    [Fact]
    public void LateFramesKeepDeadlineAlignedWithoutCatchUpBursts()
    {
        var pacer = new EffectFramePacer();
        pacer.ShouldPresent(TimeSpan.Zero, 60);
        var late = TimeSpan.FromMilliseconds(105);
        pacer.ShouldPresent(late, 60);

        var interval = TimeSpan.FromSeconds(1d / 60);
        Assert.Equal(interval - TimeSpan.FromTicks(late.Ticks % interval.Ticks),
            pacer.GetNextFrameDelay(late, 60));
    }

    [Fact]
    public void ResetAndUncappedModeRequestImmediately()
    {
        var pacer = new EffectFramePacer();
        pacer.ShouldPresent(TimeSpan.FromSeconds(1), 60);
        Assert.Equal(TimeSpan.Zero, pacer.GetNextFrameDelay(TimeSpan.FromSeconds(1), 0));
        Assert.Equal(TimeSpan.Zero, pacer.GetNextFrameDelay(TimeSpan.Zero, 60));

        pacer.Reset();
        Assert.Equal(TimeSpan.Zero, pacer.GetNextFrameDelay(TimeSpan.FromSeconds(1), 60));
    }
}
