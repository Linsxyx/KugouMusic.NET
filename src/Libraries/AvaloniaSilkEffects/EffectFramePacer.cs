namespace AvaloniaSilkEffects;

/// <summary>
/// Applies an optional frame-rate cap without accumulating timing drift.
/// A target of zero lets Avalonia's compositor own pacing.
/// </summary>
public sealed class EffectFramePacer
{
    private TimeSpan? _lastBoundary;

    public bool ShouldPresent(TimeSpan timestamp, int targetFrameRate)
    {
        if (targetFrameRate <= 0)
        {
            _lastBoundary = timestamp;
            return true;
        }

        var interval = TimeSpan.FromSeconds(1d / targetFrameRate);
        if (_lastBoundary is null || timestamp < _lastBoundary.Value)
        {
            _lastBoundary = timestamp;
            return true;
        }

        var elapsed = timestamp - _lastBoundary.Value;
        if (elapsed < interval)
            return false;

        // Keep the limiter aligned to its ideal cadence, like PixiJS' Ticker.
        _lastBoundary = timestamp - TimeSpan.FromTicks(elapsed.Ticks % interval.Ticks);
        return true;
    }

    public void Reset() => _lastBoundary = null;
}
