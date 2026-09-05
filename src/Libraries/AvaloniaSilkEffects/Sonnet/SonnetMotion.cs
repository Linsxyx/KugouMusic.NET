using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

public readonly record struct SonnetMotionFrame(double X, double Y, double Scale, double Rotation);
public readonly record struct SonnetTransitionFrame(
    double X, double Y, double Scale, double Rotation, double Alpha, double Blur, double Glitch, double GlitchSeed);

public static class SonnetMotion
{
    public static readonly SonnetTransitionFrame IdleTransition = new(0, 0, 1, 0, 1, 0, 0, 0);
    public static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static double Cubic(double p1, double p2, double time)
    {
        var inverse = 1 - time;
        return 3 * inverse * inverse * time * p1 + 3 * inverse * time * time * p2 + time * time * time;
    }

    public static double CubicBezier(double x1, double y1, double x2, double y2, double value)
    {
        var target = Clamp01(value);
        if (target is 0 or 1) return target;
        var low = 0d;
        var high = 1d;
        var parameter = target;
        for (var iteration = 0; iteration < 12; iteration++)
        {
            if (Cubic(x1, x2, parameter) < target) low = parameter;
            else high = parameter;
            parameter = (low + high) / 2;
        }
        return Cubic(y1, y2, parameter);
    }

    public static double EaseInOut(double value) => CubicBezier(0.65, 0, 0.35, 1, value);
    public static double EaseEnter(double value) => CubicBezier(0.22, 1, 0.36, 1, value);
    public static double ExpoOut(double value) => value == 1 ? 1 : 1 - Math.Pow(2, -10 * value);
    public static double ElasticOut(double value)
    {
        const double period = 0.3;
        return Math.Pow(2, -10 * value) * Math.Sin((value - period / 4) * (2 * Math.PI) / period) + 1;
    }
    public static double SegmentProgress(double start, double end, double time) =>
        ExpoOut(Clamp01((time - start) / Math.Max(end - start, 0.08)));

    public static double ShotProgress(SonnetShot shot, double time) =>
        Clamp01((time - shot.StartTime) / Math.Max(shot.EndTime - shot.StartTime, 0.001));

    public static double ShotPathProgress(SonnetShotKind kind, double progress)
    {
        var linear = Clamp01(progress);
        if (kind is SonnetShotKind.TrackingRibbon or SonnetShotKind.FragmentCollage or
            SonnetShotKind.QuietTableau or SonnetShotKind.PosterBlocks)
            return linear * 0.55 + EaseInOut(linear) * 0.45;
        if (linear < 0.18) return ExpoOut(linear / 0.18) * 0.22;
        if (linear < 0.78) return 0.22 + (linear - 0.18) / 0.6 * 0.56;
        var settle = (linear - 0.78) / 0.22;
        return 0.78 + (1 - (1 - settle) * (1 - settle)) * 0.22;
    }

    public static SonnetMotionFrame ShotFrame(SonnetShotKind kind, double progress)
    {
        var linear = Clamp01(progress);
        var eased = ShotPathProgress(kind, linear);
        return kind switch
        {
            SonnetShotKind.EditorialColumn => new SonnetMotionFrame(-0.055 + eased * 0.095, 0.025 - eased * 0.04, 0.98 + eased * 0.07, -0.006 + eased * 0.01),
            SonnetShotKind.TypeImpact => new SonnetMotionFrame(-0.035 + eased * 0.07, 0.018 - eased * 0.028, 1 + (1 - ExpoOut(Math.Min(linear / 0.18, 1))) * 0.22 + eased * 0.08, -0.01 + eased * 0.016),
            SonnetShotKind.FragmentCollage => new SonnetMotionFrame(-0.045 + eased * 0.085, 0.028 - Math.Sin(eased * Math.PI) * 0.055, 0.97 + eased * 0.09, -0.014 + eased * 0.028),
            SonnetShotKind.TrackingRibbon => new SonnetMotionFrame(-0.16 + eased * 0.28, 0.05 - eased * 0.085, 0.98 + eased * 0.07, 0.008 - eased * 0.014),
            SonnetShotKind.MaskReveal => new SonnetMotionFrame(0.035 - eased * 0.065, 0.1 - eased * 0.135, 0.96 + eased * 0.12, -0.006 + eased * 0.009),
            SonnetShotKind.PosterBlocks => new SonnetMotionFrame(-0.012 + eased * 0.024, 0.008 - eased * 0.016, 0.99 + eased * 0.025, -0.0015 + eased * 0.003),
            _ => new SonnetMotionFrame(-0.022 + eased * 0.04, 0.014 - eased * 0.025, 1 + eased * 0.028, -0.002 + eased * 0.003),
        };
    }

    public static SonnetMotionFrame CameraBreath(double time, double phase = 0)
    {
        var tau = time * Math.PI * 2;
        return new SonnetMotionFrame(
            (Math.Sin(tau * 0.13 + phase) * 0.65 + Math.Sin(tau * 0.31 + phase * 1.7) * 0.35) * 0.006,
            (Math.Cos(tau * 0.11 + phase * 2.3) * 0.65 + Math.Sin(tau * 0.29 + phase * 0.9) * 0.35) * 0.006,
            Math.Sin(tau * 0.09 + phase * 1.3) * 0.002,
            Math.Sin(tau * 0.07 + phase * 2.9) * 0.0015);
    }

    public static double BreathWeight(double time, double revealDoneTime, double rampDuration = 1.2) =>
        rampDuration <= 0 ? time >= revealDoneTime ? 1 : 0 : EaseInOut(Clamp01((time - revealDoneTime) / rampDuration));

    public static IReadOnlyList<double> FocusWeights(IReadOnlyList<(double Start, double End)> ranges, double time, double sigma = 0.35)
    {
        if (ranges.Count == 0) return [];
        var span = new (double Start, double End)[ranges.Count];
        for (var index = 0; index < ranges.Count; index++)
            span[index] = ranges[index];
        var weights = new double[ranges.Count];
        FillFocusWeights(span, weights, time, sigma);
        return weights;
    }

    internal static void FillFocusWeights(ReadOnlySpan<(double Start, double End)> ranges, Span<double> weights, double time, double sigma)
    {
        var safeSigma = Math.Max(0.001, sigma);
        var max = double.NegativeInfinity;
        for (var index = 0; index < ranges.Length; index++)
        {
            var range = ranges[index];
            var start = Math.Min(range.Start, range.End);
            var end = Math.Max(range.Start, range.End);
            var distance = time < start ? start - time : time > end ? time - end : 0;
            var log = -(distance * distance) / (2 * safeSigma * safeSigma);
            weights[index] = log;
            if (log > max) max = log;
        }

        var total = 0d;
        for (var index = 0; index < ranges.Length; index++)
        {
            var weight = Math.Exp(weights[index] - max);
            weights[index] = weight;
            total += weight;
        }

        if (total <= 0) return;
        for (var index = 0; index < ranges.Length; index++)
            weights[index] /= total;
    }

    public static Vector2 SmoothedCameraFocus(
        double time, double startTime, double endTime, Func<double, Vector2> sampleFocus,
        double smoothingWindow = 0.12, double maxBlendDistance = 96)
    {
        var safeStart = Math.Min(startTime, endTime);
        var safeEnd = Math.Max(startTime, endTime);
        var radius = Math.Max(0, smoothingWindow);
        if (radius == 0 || safeStart == safeEnd)
            return sampleFocus(Math.Clamp(time, safeStart, safeEnd));

        ReadOnlySpan<(double Offset, double Weight)> kernel =
            [(-1, 1), (-0.5, 4), (0, 6), (0.5, 4), (1, 1)];
        Span<Vector2> samples = stackalloc Vector2[kernel.Length];
        for (var index = 0; index < kernel.Length; index++)
            samples[index] = sampleFocus(Math.Clamp(time + kernel[index].Offset * radius, safeStart, safeEnd));
        var center = samples[2];
        var maxDistanceSquared = Math.Max(0, maxBlendDistance) * Math.Max(0, maxBlendDistance);
        var total = 0d;
        var result = Vector2.Zero;
        for (var index = 0; index < samples.Length; index++)
        {
            if (Vector2.DistanceSquared(samples[index], center) > maxDistanceSquared) continue;
            result += samples[index] * (float)kernel[index].Weight;
            total += kernel[index].Weight;
        }
        return result / (float)total;
    }

    public static Vector2 SegmentCameraFocus(
        IReadOnlyList<(Vector2 Position, double StartTime, bool IsBackgroundShape)> glyphs,
        double time, double trackingFactor = 0.5)
    {
        var semanticCount = 0;
        for (var index = 0; index < glyphs.Count; index++)
            if (!glyphs[index].IsBackgroundShape) semanticCount++;
        if (semanticCount == 0) return Vector2.Zero;
        Span<(Vector2 Position, double StartTime)> semantic =
            semanticCount <= 64 ? stackalloc (Vector2, double)[semanticCount] : new (Vector2, double)[semanticCount];
        var fill = 0;
        for (var index = 0; index < glyphs.Count; index++)
        {
            var glyph = glyphs[index];
            if (!glyph.IsBackgroundShape) semantic[fill++] = (glyph.Position, glyph.StartTime);
        }
        return SegmentCameraFocusCore(semantic, time, trackingFactor);
    }

    internal static Vector2 SegmentCameraFocusCore(
        ReadOnlySpan<(Vector2 Position, double StartTime)> semantic,
        double time, double trackingFactor = 0.5)
    {
        if (semantic.Length == 0) return Vector2.Zero;
        var first = semantic[0];
        var last = semantic[^1];
        var center = (first.Position + last.Position) * 0.5f;
        Vector2 Apply(Vector2 exact) => Vector2.Lerp(center, exact, (float)trackingFactor);
        if (time <= first.StartTime) return Apply(first.Position);
        if (time >= last.StartTime) return Apply(last.Position);
        for (var index = 0; index < semantic.Length - 1; index++)
        {
            var current = semantic[index];
            var next = semantic[index + 1];
            if (time < current.StartTime || time > next.StartTime) continue;
            var progress = (time - current.StartTime) / Math.Max(0.001, next.StartTime - current.StartTime);
            return Apply(Vector2.Lerp(current.Position, next.Position, (float)progress));
        }
        return Apply(first.Position);
    }

    public static double SegmentDepth(SonnetSegmentRole role, Func<double>? random = null)
    {
        if (role != SonnetSegmentRole.Decoration) return 0;
        random ??= Random.Shared.NextDouble;
        return random() > 0.5 ? 0.5 + random() * 0.8 : -0.5 - random() * 0.8;
    }

    public static Vector2 SegmentNormalOffset(
        SonnetSegmentRole role, bool vertical, double rotation, double fontSize, double randomValue)
    {
        if (role != SonnetSegmentRole.Support) return Vector2.Zero;
        var distance = (Math.Clamp(randomValue, 0, 1) * 2 - 1) * fontSize * 0.3;
        var angle = rotation + (vertical ? 0 : Math.PI / 2);
        return new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
    }

    public static (double X, double Y, double Rotation) TimelineShake(double time, double intensity)
    {
        if (intensity <= 0) return (0, 0, 0);
        var x = Math.Sin(time * 123.456) * Math.Cos(time * 789.123);
        var y = Math.Cos(time * 345.678) * Math.Sin(time * 901.234);
        var rotation = Math.Sin(time * 567.890);
        return (x * 0.02 * intensity, y * 0.02 * intensity, rotation * 0.005 * intensity);
    }

    public static double GlyphMotionDuration(double start, double end)
        => SonnetGlyphLayout.ResolveMotionDuration(start, end);

    public static IReadOnlyList<SonnetGlyphPlacement> BuildGlyphs(
        SonnetSemanticSegment segment, SonnetTypographyPlacement placement, float fontSize,
        Func<string, float> measure, double shotStart, double shotEnd)
        => SonnetGlyphLayout.Build(segment, placement, fontSize, measure, shotStart, shotEnd);
}
