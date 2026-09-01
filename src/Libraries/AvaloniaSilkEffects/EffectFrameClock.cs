using System.Diagnostics;

namespace AvaloniaSilkEffects;

public sealed class EffectFrameClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _offset;
    private TimeSpan _last;
    private ulong _frameNumber;
    private bool _paused;

    public TimeSpan Elapsed => _offset + _stopwatch.Elapsed;

    public void Seek(TimeSpan value)
    {
        _offset = value;
        if (_paused)
            _stopwatch.Reset();
        else
            _stopwatch.Restart();
        _last = value;
    }

    public void SetPaused(bool paused)
    {
        if (paused == _paused)
            return;

        _paused = paused;
        if (paused)
        {
            _offset += _stopwatch.Elapsed;
            _stopwatch.Reset();
            _last = _offset;
        }
        else
        {
            _stopwatch.Restart();
            _last = _offset;
        }
    }

    public (TimeSpan Elapsed, TimeSpan Delta, ulong FrameNumber) Step()
    {
        var elapsed = Elapsed;
        var delta = elapsed - _last;
        _last = elapsed;
        delta = ClampDelta(delta);
        return (elapsed, delta, ++_frameNumber);
    }

    public static TimeSpan ClampDelta(TimeSpan delta) =>
        delta < TimeSpan.Zero
            ? TimeSpan.Zero
            : delta > TimeSpan.FromMilliseconds(100)
                ? TimeSpan.FromMilliseconds(100)
                : delta;
}
