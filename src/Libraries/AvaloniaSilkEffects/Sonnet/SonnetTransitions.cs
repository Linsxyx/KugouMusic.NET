namespace AvaloniaSilkEffects.Sonnet;

public static class SonnetTransitions
{
    public static SonnetTransitionFrame ResolveParagraph(SonnetParagraph paragraph, double time, bool enabled, uint seed, bool allowGlitch = true)
    {
        var transition = paragraph.TransitionOut;
        if (!enabled || transition is null || time < transition.StartTime) return SonnetMotion.IdleTransition;
        return Resolve(transition.Kind, false, (time - transition.StartTime) / Math.Max(transition.EndTime - transition.StartTime, 0.001), seed, allowGlitch);
    }

    /// <summary>Folia's resolveSonnetEnterTransitionFrame: the incoming half of the previous paragraph's exit.</summary>
    public static SonnetTransitionFrame ResolveEnter(SonnetTransitionKind? kind, double timeSinceStart, double duration,
        bool enabled, uint seed, bool allowGlitch = true)
    {
        if (!enabled || kind is not { } value || timeSinceStart < 0 || timeSinceStart > duration) return SonnetMotion.IdleTransition;
        return Resolve(value, true, timeSinceStart / Math.Max(duration, 0.001), seed, allowGlitch);
    }

    public static SonnetTransitionFrame ResolveShot(IReadOnlyList<SonnetShot> shots, int active, double time, bool enabled, uint seed, bool allowGlitch = true)
    {
        if (!enabled || shots.Count < 2 || active < 0 || active >= shots.Count) return SonnetMotion.IdleTransition;
        var current = shots[active];
        if (active > 0)
        {
            var duration = Math.Min(0.24, Math.Max(0.14, (current.StartTime - shots[active - 1].StartTime) * 0.18));
            if (time <= current.StartTime + duration)
                return Resolve(BoundaryKind(seed, active - 1), true, (time - current.StartTime) / duration, seed + (uint)(active * 97), allowGlitch);
        }
        if (active + 1 >= shots.Count) return SonnetMotion.IdleTransition;
        var next = shots[active + 1];
        var exitDuration = Math.Min(0.24, Math.Max(0.14, (next.StartTime - current.StartTime) * 0.18));
        if (time < next.StartTime - exitDuration) return SonnetMotion.IdleTransition;
        return Resolve(BoundaryKind(seed, active), false, (time - (next.StartTime - exitDuration)) / exitDuration, seed + (uint)((active + 1) * 97), allowGlitch);
    }

    public static SonnetTransitionFrame Resolve(SonnetTransitionKind kind, bool entering, double progress, uint seed, bool allowGlitch = true)
    {
        // Keep the transition timing, but avoid horizontal shader tearing when disabled.
        if (!allowGlitch && kind == SonnetTransitionKind.MonoGlitch)
            kind = SonnetTransitionKind.FastBlur;
        var linear = SonnetMotion.Clamp01(progress);
        var eased = SonnetMotion.EaseInOut(linear);
        var amount = entering ? 1 - eased : eased;
        return kind switch
        {
            SonnetTransitionKind.FastBlur => new SonnetTransitionFrame(0, 0, 1, 0, entering ? 1 - amount * 0.82 : 1 - amount, amount * 14, 0, 0),
            SonnetTransitionKind.MonoGlitch => new SonnetTransitionFrame(0, 0, 1, 0,
                !entering && linear > 0.86 ? 1 - (linear - 0.86) / 0.14 : 1,
                0, amount, seed * 0.0001 + Math.Floor(linear * 14) * 0.173),
            _ => new SonnetTransitionFrame(0, 0, 1, 0, entering ? 1 - amount * 0.72 : 1 - amount, 0, 0, 0),
        };
    }

    private static SonnetTransitionKind BoundaryKind(uint seed, int index) =>
        (SonnetTransitionKind)((seed ^ unchecked((uint)(index + 1) * 0x9e3779b1u)) % 3);
}
