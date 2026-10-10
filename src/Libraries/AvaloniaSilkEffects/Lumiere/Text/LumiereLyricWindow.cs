using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Lumiere.Text;

/// <summary>A line's slot: offset from the region center (height units), scale, alpha, orientation, tilt, wrap.</summary>
internal readonly record struct LumiereSlot(double Dx, double Dy, double Scale, double Alpha, double Orient, double Rotation, double Wrap);

internal sealed class LumiereLyricWindowOptions
{
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required IReadOnlyList<LumiereLine> Lines { get; init; }
    public required string Font { get; init; }
    public required int Weight { get; init; }
    public required float Resolution { get; init; }
    /// <summary>Text region in height units (center + size).</summary>
    public required LumiereRegion Region { get; init; }
    /// <summary>Current line font size (logical px).</summary>
    public required double HeroPx { get; init; }
    /// <summary>1 = previous line only, 2 = previous and next.</summary>
    public int Neighbors { get; init; } = 2;
    public required LumiereTypography Typography { get; init; }
    /// <summary>The typography line i uses once it becomes current (its shot's); null = always <see cref="Typography"/>.</summary>
    public Func<int, LumiereTypography>? TypographyOf { get; init; }
    public required LumiereDecaySpec Decay { get; init; }
    /// <summary>Every slot change flies with trails (otherwise only crossed typography or orientation changes do).</summary>
    public bool AlwaysFly { get; init; }
    /// <summary>Drift and orbit multiplier (credits use 0).</summary>
    public double Drift { get; init; } = 1;
    public required string Seed { get; init; }
    public double LetterSpacing { get; init; } = 0.04;
    /// <summary>Keyword matchers (theme wordColors); empty for none.</summary>
    public IReadOnlyList<LumiereKeywordMatcher> Keywords { get; init; } = [];
}

public readonly record struct LumiereWindowFrame(
    double Time,
    IReadOnlyList<LumiereResolvedBeam> Beams,
    Vector3 LitColor,
    Vector3 UnlitColor,
    double UnlitAlpha,
    double Intensity,
    bool HideTrails = false);

/// <summary>
/// Folia text/lyricWindow.ts (+ windowLines.ts, lineClearance.ts): a local tiled window of the few lines around the
/// current one. Future lines are unlit engravings, past lines afterglow, the current line largest. When the current
/// line changes, lines move to new slots (staggered: leaving lines first) while the oldest fades into the smoke.
/// Each glyph has horizontal and vertical positions (single / wrapped); crossed typography gives neighbours seeded free
/// placements. Slot changes fly glyphs along their own Bézier curves with cloud-chamber trails. Lit glyphs decay —
/// drifting off (mostly upwards) — and unsung lines gather in from scattered positions. Neighbours drift away from
/// the current line, and glyphs falling into the current line's ink box dim. Glyph brightness = lit progress × (base
/// + light field at the glyph); sung glyphs flash a four-point sparkle; lit glyphs carry a soft halo. Pure in t.
/// Lines are built on demand around the current one (each line's random draws come from one stream skipped to it).
/// </summary>
internal sealed class LumiereLyricWindow : IDisposable
{
    private const double Lead = 1.2;
    private const double Slide = 1.5;
    private const double MaxLag = 0.6;
    private static readonly (double Slow, double Fast) DriftSpeed = (0.013, 0.022);
    private const double Orbit = 0.03;
    private const double LightUp = 0.3;
    private const double SpotWindow = 0.3;
    private const int SpotSamples = 8;
    private const double GatherLead = 1.6;
    private const double Gather = 1.8;
    private const double TrackTime = 0.45;
    private const int TrackSamples = 20;
    private const double DissolveFrom = 1;
    private const double DissolveSpan = 2;
    private const int WindowReach = 3;
    private const int Prebuild = 2;
    private const int KeepMargin = 2;
    private const double CrossedClearance = 0.5;
    /// <summary>Glyph / line random draws: must match buildLineView's draw count.</summary>
    private const int GlyphRandomDraws = 19;
    private const int LineRandomDraws = 4;
    // lineClearance.ts
    private const double AwaySoftness = 0.005;
    private const double Hold = 0.018;
    private const double ProtectFloor = 0.4;
    private const double ProtectMargin = 0.5;

    private static double SlideLag(int relative) => Math.Clamp(relative, -2, 2) switch
    {
        -2 => 0,
        -1 => 0.15,
        0 => 0.3,
        1 => 0.5,
        _ => 0.6,
    };

    private readonly GL _gl;
    private readonly LumiereLyricWindowOptions _options;
    private readonly double _height;
    private readonly LumiereRegion _region;
    private readonly double _heroPx;
    private readonly double _fontH;
    private readonly double _nextAlpha;
    private readonly LumiereFrameBand _band;
    private readonly double _columnCap;
    private readonly LumiereTextMeasurer _measurer;
    private readonly LineMeta[] _metas;
    private readonly LineView?[] _views;
    private readonly List<int> _built = [];
    private readonly Dictionary<(int, int, LumiereTypography), LumiereSlot> _slotCache = [];
    private readonly ProtectBox[] _protectBoxes = [new(), new(), new(), new()];
    private int _protectCount;
    private readonly LineTransform?[] _frameTransforms;
    private readonly PolylineNode _track = new() { TailAlpha = 1, HeadAlpha = 1 };
    private readonly List<Vector2> _trackPoints = new(TrackSamples + 1);

    private sealed class LineMeta
    {
        public required LumiereLine Line;
        public required IReadOnlyList<string> Graphemes;
        public required IReadOnlyList<LumiereGlyphTiming> Timings;
        public int RandomOffset;
        /// <summary>Per-grapheme keyword colors (null entries are plain glyphs); matched on first use.</summary>
        public Vector3?[]? KeywordColors;
        public double SingStart, SingEnd;
    }

    private sealed class GlyphView
    {
        public LumiereGlyphSlice Slice;
        public LumiereGlyphTiming Timing;
        public bool Blank;
        public int Index;
        public double VRotation;
        public double C1X, C1Y, C2X, C2Y, FlightDelay, FlightDuration, FlightSpin, Wobble;
        public double Scale;
        public double StarDx, StarDy, StarRotation, StarSize;
        public double DriftDx, DriftDy, DriftSpeed, DriftSpin;
        public double ScatterX, ScatterY;
        public double Phase;
    }

    private sealed class LineView
    {
        public int Index;
        public required LumiereLine Line;
        public required LumiereGlyphLine Layout;
        public required GlyphView[] Glyphs;
        public required LumiereLineVariant[][] Flow;
        public required Dictionary<int, LumiereSlot> Placements;
        public double SingStart, SingEnd, Jitter, VelocityX, VelocityY, MotionPhase;
    }

    private sealed class LineTransform
    {
        public int Current;
        public double X, Y, Scale, Rotation, Alpha;
        public double FromOrient, ToOrient, FromWrap, ToWrap, Wrap, Orient, Phase;
        public bool Fly;
        public double SlideStart;
        public double RestOrient, RestWrap;
        public readonly List<(double ToOrient, double ToWrap, double Phase, bool Fly)> Moves = [];
    }

    private sealed class ProtectBox
    {
        public int Line = -1;
        public double X, Y, Cos = 1, Sin, HalfW, HalfH, Margin = 1, Weight;
    }

    public LumiereLyricWindow(GL gl, LumiereLyricWindowOptions options)
    {
        _gl = gl;
        _options = options;
        _height = options.Height;
        _region = options.Region;
        _heroPx = options.HeroPx;
        _fontH = _heroPx / _height;
        _nextAlpha = options.Neighbors >= 2 ? 0.95 : 0;
        // Length caps: horizontal lines use the frame's full width (crossed leaves 10% for the lines around it),
        // vertical lines the frame's full height (clear of the bottom subtitles). The region only sets the center.
        _band = LumiereLineWrap.FrameBand(options.Width, options.Height);
        _columnCap = (_band.Bottom - _band.Top) * _height;
        _measurer = new LumiereTextMeasurer(options.Font, options.Weight, options.LetterSpacing,
            string.Concat(options.Lines.Select(line => line.Text)));
        var offset = 0;
        _metas = [.. options.Lines.Select(line =>
        {
            var (graphemes, timings) = LumiereReveal.Timeline(line);
            var sung = graphemes.Select((glyph, index) => (glyph, timing: timings[index]))
                .Where(item => !string.IsNullOrWhiteSpace(item.glyph)).Select(item => item.timing).ToArray();
            var meta = new LineMeta
            {
                Line = line, Graphemes = graphemes, Timings = timings, RandomOffset = offset,
                SingStart = sung.Length > 0 ? sung.Min(timing => timing.Start) : line.StartTime,
                SingEnd = sung.Length > 0 ? sung.Max(timing => timing.End) : line.EndTime,
            };
            offset += graphemes.Count * GlyphRandomDraws + LineRandomDraws;
            return meta;
        })];
        _views = new LineView?[_metas.Length];
        _frameTransforms = new LineTransform?[_metas.Length];
    }

    private int LineCount => _metas.Length;
    private double MaxWidthOf(LumiereTypography kind) =>
        (_band.Right - _band.Left) * _height * (kind == LumiereTypography.Crossed ? 0.9 : 1);

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
    private static double Lerp(double a, double b, double k) => a + (b - a) * k;
    private static double Smooth(double value)
    {
        var t = Clamp01(value);
        return t * t * (3 - 2 * t);
    }
    private static double EaseInOutCubic(double value)
    {
        var t = Clamp01(value);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }
    /// <summary>Softer sine easing for slot changes: on top of the drift there is never a moment of standing still.</summary>
    private static double EaseInOutSine(double value) => (1 - Math.Cos(Math.PI * Clamp01(value))) / 2;

    // ---- building ---------------------------------------------------------------------------------------------

    private LineView Build(int index)
    {
        var meta = _metas[index];
        var line = meta.Line;
        var rng = LumiereRng.At($"{_options.Seed}:window", meta.RandomOffset);
        // Glyph textures at the largest word scale; enlarged words only shrink.
        var layout = new LumiereGlyphLine(_gl, line.Text, (float)(_heroPx * LumiereWordStyle.MaxWordScale), _options.Font,
            _options.Weight, _options.Resolution, (float)_options.LetterSpacing);
        var words = LumiereWords.Segment(line.Text);
        var wordSeed = $"{_options.Seed}:{index}:{line.Text}";
        var scales = LumiereWordStyle.Scales(words, wordSeed);
        var jags = LumiereWordStyle.Jags(words, scales, wordSeed);
        var wordOf = layout.Glyphs.Select((_, i) => Math.Max(0, IndexOfWord(words, i))).ToArray();
        double GlyphScale(int i) => wordOf[i] < scales.Length ? scales[wordOf[i]] : 1;
        double GlyphJag(int i) => (wordOf[i] < jags.Length ? jags[wordOf[i]] : 0) * _heroPx;

        var flow = LumiereLineWrap.Flow(_measurer,
            [.. layout.Glyphs.Select((slice, i) => new LumiereFlowGlyph(slice.Char, GlyphScale(i),
                slice.CharWidth / LumiereWordStyle.MaxWordScale * GlyphScale(i), slice.Upright, GlyphJag(i)))],
            words, _heroPx, MaxWidthOf(LumiereTypography.Horizontal), _columnCap);

        var glyphs = new GlyphView[layout.Glyphs.Count];
        for (var i = 0; i < glyphs.Length; i++)
        {
            var slice = layout.Glyphs[i];
            // Flight: the first control point flung in a random direction, the second turned about half a circle more.
            var a1 = rng.Next() * Math.PI * 2;
            var a2 = a1 + Math.PI * (rng.Next() < 0.5 ? 0.5 : 1.5) + (rng.Next() - 0.5) * 0.8;
            var m1 = _heroPx * (1.6 + rng.Next() * 3.2);
            var m2 = _heroPx * (1 + rng.Next() * 2.6);
            // Takeoff and duration spread over the slide (delay + duration ≤ 1, always landed when it ends).
            var duration = 0.45 + rng.Next() * 0.4;
            var glyph = new GlyphView
            {
                Slice = slice,
                Timing = i < meta.Timings.Count ? meta.Timings[i] : new LumiereGlyphTiming(line.StartTime, line.EndTime),
                Blank = slice.Blank,
                Index = i,
                VRotation = slice.Upright ? 0 : Math.PI / 2,
                C1X = Math.Cos(a1) * m1,
                C1Y = Math.Sin(a1) * m1,
                C2X = Math.Cos(a2) * m2,
                C2Y = Math.Sin(a2) * m2,
                FlightDelay = rng.Next() * (1 - duration),
                FlightDuration = duration,
                FlightSpin = (rng.Next() - 0.5) * 2.4,
                Wobble = rng.Next() * Math.PI * 2,
                Scale = GlyphScale(i),
            };
            // Drift direction: mostly upwards (smoke rises), spread sideways.
            var angle = -Math.PI / 2 + (rng.Next() - 0.5) * 2.2;
            var scatterAngle = rng.Next() * Math.PI * 2;
            var scatterDistance = 0.6 + rng.Next() * 1.4;
            glyph.StarDx = -0.15 + rng.Next() * 0.5;
            // Mostly on the glyph's upper half, a few under its foot.
            glyph.StarDy = -0.62 + Math.Pow(rng.Next(), 1.4) * 0.9;
            glyph.StarRotation = (rng.Next() - 0.5) * 0.5;
            glyph.StarSize = 0.65 + rng.Next() * 0.7;
            glyph.DriftDx = Math.Cos(angle);
            glyph.DriftDy = Math.Sin(angle);
            glyph.DriftSpeed = 0.6 + rng.Next() * 0.9;
            glyph.DriftSpin = (rng.Next() - 0.5) * 2;
            glyph.ScatterX = Math.Cos(scatterAngle) * scatterDistance;
            glyph.ScatterY = Math.Sin(scatterAngle) * scatterDistance;
            glyph.Phase = rng.Next() * Math.PI * 2;
            glyphs[i] = glyph;
        }
        var view = new LineView
        {
            Index = index,
            Line = line,
            Layout = layout,
            Glyphs = glyphs,
            Flow = flow,
            // Placements use their own stream: switching typography leaves every other draw untouched.
            Placements = FreePlacements(new LumiereRng($"{_options.Seed}:placements:{index}")),
            SingStart = meta.SingStart,
            SingEnd = meta.SingEnd,
            Jitter = (rng.Next() - 0.5) * 0.16,
        };
        var velocityAngle = rng.Next() * Math.PI * 2;
        var speed = DriftSpeed.Slow + rng.Next() * (DriftSpeed.Fast - DriftSpeed.Slow);
        view.VelocityX = Math.Cos(velocityAngle) * speed;
        view.VelocityY = Math.Sin(velocityAngle) * speed;
        view.MotionPhase = rng.Next() * Math.PI * 2;
        var position = 0;
        while (position < _built.Count && _built[position] < index) position++;
        _built.Insert(position, index);
        _views[index] = view;
        return view;
    }

    private static int IndexOfWord(IReadOnlyList<LumiereWordSpan> words, int glyph)
    {
        for (var w = 0; w < words.Count; w++)
            if (glyph >= words[w].Start && glyph < words[w].End) return w;
        return -1;
    }

    private void Release(int index)
    {
        var view = _views[index];
        if (view is null) return;
        _views[index] = null;
        _built.Remove(index);
        view.Layout.Dispose();
    }

    private LineView LineOf(int index) => _views[index] ?? Build(index);

    /// <summary>
    /// Crossed typography's free placements: the previous line (−1) on the upper half circle, the next (+1) on the lower,
    /// angle and distance seeded; either orientation, horizontal ones tilted more. ±2 push out the same way, transparent.
    /// </summary>
    private Dictionary<int, LumiereSlot> FreePlacements(LumiereRng random)
    {
        LumiereSlot Placement(bool upper, double alpha)
        {
            var angle = upper ? Math.PI * (1.12 + random.Next() * 0.76) : Math.PI * (0.12 + random.Next() * 0.76);
            var rx = _region.W * (0.24 + random.Next() * 0.22);
            var ry = _region.H * (0.3 + random.Next() * 0.18);
            var vertical = random.Next() < 0.5;
            var scale = 0.5 + random.Next() * 0.2;
            var rotation = (random.Next() - 0.5) * (vertical ? 0.3 : 0.7);
            return new LumiereSlot(Math.Cos(angle) * rx, Math.Sin(angle) * ry, scale, alpha, vertical ? 1 : 0, rotation, 0);
        }
        static LumiereSlot Farther(LumiereSlot slot) =>
            slot with { Dx = slot.Dx * 1.45, Dy = slot.Dy * 1.45, Scale = slot.Scale * 0.85, Alpha = 0 };
        var previous = Placement(true, 0.9);
        var next = Placement(false, _nextAlpha);
        return new Dictionary<int, LumiereSlot> { [-1] = previous, [1] = next, [-2] = Farther(previous), [2] = Farther(next) };
    }

    // ---- slots ------------------------------------------------------------------------------------------------

    private LumiereTypography TypographyAt(int c) =>
        _options.TypographyOf is { } of ? of(Math.Max(0, Math.Min(LineCount - 1, c))) : _options.Typography;

    private (int Wrap, LumiereLineVariant Variant, double Scale) Settle(int index, LumiereTypography kind, int orient, double scale)
    {
        var flow = LineOf(index).Flow[orient];
        var budget = orient == 1 ? _columnCap : MaxWidthOf(kind);
        var wrap = LumiereLineWrap.ShouldWrap(flow, budget, scale) ? 1 : 0;
        var variant = flow[wrap];
        return (wrap, variant, scale * LumiereLineWrap.FitScale(variant.Along, budget, scale));
    }

    private double AcrossOf(int index, LumiereTypography kind, int orient, double scale)
    {
        if (index < 0 || index >= LineCount) return _fontH * scale;
        var settled = Settle(index, kind, orient, scale);
        return settled.Variant.Across / _height * settled.Scale;
    }

    /// <summary>The shift that fits the current line into the frame (height units); the whole window moves with it.</summary>
    private (double X, double Y) WindowShift(int current, LumiereTypography kind)
    {
        if (current < 0 || current >= LineCount) return (0, 0);
        var orient = kind == LumiereTypography.Vertical ? 1 : 0;
        var (_, variant, scale) = Settle(current, kind, orient, 1);
        var w = variant.InkWidth / _height * scale;
        var h = variant.InkHeight / _height * scale;
        return (LumiereLineWrap.ClampInto(_region.Cx, w, _band.Left, _band.Right) - _region.Cx,
            LumiereLineWrap.ClampInto(_region.Cy, h, _band.Top, _band.Bottom) - _region.Cy);
    }

    private static readonly (double Previous, double Next, double FarPrevious, double FarNext) VerticalGap = (0.95, 0.88, 0.78, 0.7);
    private static readonly (double Previous, double Next, double FarPrevious, double FarNext) HorizontalGap = (0.93, 0.735, 0.56, 0.56);

    /// <summary>Fixed slots of horizontal / vertical typography (relative to the region center, height units).</summary>
    private LumiereSlot FixedSlot(LumiereTypography typography, int relative, Func<int, double, double> across)
    {
        var vertical = typography == LumiereTypography.Vertical;
        var orient = vertical ? 1 : 0;
        if (relative == 0) return new LumiereSlot(0, 0, 1, 1, orient, 0, 0);
        var previous = relative < 0;
        var far = Math.Abs(relative) > 1;
        var nearScale = vertical ? 0.58 : 0.52;
        var scale = far ? (vertical ? 0.48 : 0.44) : nearScale;
        var gap = vertical ? VerticalGap : HorizontalGap;
        var sign = previous ? -1 : 1;
        var offset = across(0, 1) / 2 + (previous ? gap.Previous : gap.Next) * _fontH;
        if (far) offset += across(sign, nearScale) + (previous ? gap.FarPrevious : gap.FarNext) * _fontH;
        offset += across(relative, scale) / 2;
        var alpha = previous ? (far ? 0 : 0.9) : (far ? 0 : _nextAlpha);
        // Vertical reads right to left: the previous line on the right, the next on the left.
        return vertical
            ? new LumiereSlot(-sign * offset, sign * (far ? 0.07 : 0.03), scale, alpha, orient, 0, 0)
            : new LumiereSlot(sign * _region.W * (far ? 0.24 : 0.16), sign * offset, scale, alpha, orient, 0, 0);
    }

    /// <summary>Line <paramref name="index"/>'s slot when <paramref name="current"/> is current under <paramref name="kind"/>.</summary>
    private LumiereSlot SlotOf(int index, int current, LumiereTypography kind)
    {
        if (_slotCache.TryGetValue((index, current, kind), out var cached)) return cached;
        var view = LineOf(index);
        var clamped = Math.Clamp(index - current, -2, 2);
        var shift = WindowShift(current, kind);
        LumiereSlot slot;
        int? wrap = null;
        if (kind == LumiereTypography.Crossed && clamped != 0)
        {
            // Free placements on the current line's upper / lower half, but clear of it (taller when it wraps): at least a
            // gap between the neighbour's block and the current line. Tall vertical neighbours first wrap to two columns,
            // then shrink.
            var basis = view.Placements[clamped];
            var baseOrient = basis.Orient >= 0.5 ? 1 : 0;
            var sign = basis.Dy < 0 ? -1 : 1;
            double currentHalf;
            if (current >= 0 && current < LineCount)
            {
                var settled = Settle(current, kind, 0, 1);
                currentHalf = settled.Variant.InkHeight / _height * settled.Scale / 2;
            }
            else currentHalf = _fontH / 2;
            var gap = _fontH * CrossedClearance;
            var centerY = _region.Cy + shift.Y;
            var room = sign < 0 ? centerY - currentHalf - gap - _band.Top : _band.Bottom - (centerY + currentHalf + gap);
            var flow = view.Flow[baseOrient];
            var budget = baseOrient == 1 ? _columnCap : MaxWidthOf(kind);
            var tilt = Math.Abs(basis.Rotation) + 0.03;
            double HeightOf(int w, double s) =>
                (flow[w].InkHeight * Math.Cos(tilt) + flow[w].InkWidth * Math.Sin(tilt)) / _height * s
                * LumiereLineWrap.FitScale(flow[w].Along, budget, s);
            var scale = basis.Scale;
            var w = Settle(index, kind, baseOrient, scale).Wrap;
            if (baseOrient == 1 && room > 0)
            {
                if (w == 0 && flow[1].Lines > 1 && HeightOf(0, scale) > room) w = 1;
                var tall = HeightOf(w, scale);
                if (tall > room) scale = Math.Max(basis.Scale * 0.5, scale * (room / tall));
            }
            wrap = w;
            var clearance = currentHalf + gap + HeightOf(w, scale) / 2;
            var dy = sign * Math.Max(Math.Abs(basis.Dy), clearance);
            // A vertical neighbour wrapped to two columns is wider: push it out by the extra half along its direction.
            var wider = baseOrient == 1 ? Math.Max(0, AcrossOf(index, kind, 1, scale) - _fontH * scale) / 2 : 0;
            slot = basis with { Scale = scale, Dy = dy, Dx = basis.Dx + Math.Sign(basis.Dx) * wider };
        }
        else
        {
            var orient = kind == LumiereTypography.Vertical ? 1 : 0;
            slot = FixedSlot(kind == LumiereTypography.Crossed ? LumiereTypography.Horizontal : kind, clamped,
                (offset, s) => AcrossOf(offset == clamped ? index : current + offset, kind, orient, s));
        }
        var result = slot with
        {
            Dx = slot.Dx + shift.X,
            Dy = slot.Dy + shift.Y,
            Wrap = wrap ?? Settle(index, kind, slot.Orient >= 0.5 ? 1 : 0, slot.Scale).Wrap,
        };
        _slotCache[(index, current, kind)] = result;
        return result;
    }

    // ---- time -------------------------------------------------------------------------------------------------

    private int Cursor(double time)
    {
        var current = -1;
        for (var i = 0; i < LineCount; i++)
        {
            if (_metas[i].Line.StartTime - Lead <= time) current = i;
            else break;
        }
        return current;
    }

    private (double Phase, double Start) LinePhase(int current, int relative, double time)
    {
        if (current < 0) return (1, double.NegativeInfinity);
        var start = _metas[current].Line.StartTime - Lead + SlideLag(relative);
        return (Clamp01((time - start) / Slide), start);
    }

    /// <summary>The first line change from <paramref name="time"/> on that is not long finished (current + 1 = all done).</summary>
    private int FirstLiveChange(double time, int current)
    {
        var c = current;
        while (c >= 0 && _metas[c].Line.StartTime - Lead + MaxLag + Slide + TrackTime > time) c--;
        return c + 1;
    }

    private static int StackX(LumiereTypography kind) => kind == LumiereTypography.Vertical ? 1 : 0;

    private static int AwayOf(int index, int c, LumiereTypography kind) =>
        (index == c ? 0 : index < c ? -1 : 1) * (kind == LumiereTypography.Vertical ? -1 : 1);

    private static double SoftAbs(double a) => Math.Sqrt(a * a + AwaySoftness * AwaySoftness) - AwaySoftness;

    /// <summary>A neighbour's drift on one axis: the share heading towards the current line turns away (speed kept).</summary>
    private static double AwayDrift(double drift, double away)
    {
        if (away == 0) return drift;
        var sign = away > 0 ? 1 : -1;
        var along = drift * sign;
        return drift + Math.Abs(away) * (SoftAbs(along) - along) * sign;
    }

    /// <summary>The current line's drift: starts along its velocity, then circles with radius HOLD at the same speed.</summary>
    private static double HeldDrift(double vx, double vy, double age, int axis)
    {
        var speed = Math.Sqrt(vx * vx + vy * vy);
        if (speed < 1e-9) return 0;
        var phi = speed * age / Hold;
        var along = Hold * Math.Sin(phi);
        var turn = Hold * (1 - Math.Cos(phi));
        var ux = vx / speed;
        var uy = vy / speed;
        return axis == 0 ? along * ux - turn * uy : along * uy + turn * ux;
    }

    /// <summary>
    /// Line <paramref name="index"/>'s position (logical px), scale, rotation, alpha and slot-change state at
    /// <paramref name="time"/>. Additive: slot before the first live change + Σ (slot difference × that change's eased
    /// progress for this line). Long-finished changes cancel out, so summing starts after them. Pure.
    /// </summary>
    private LineTransform Transform(int index, double time)
    {
        var view = LineOf(index);
        var current = Cursor(time);
        var first = FirstLiveChange(time, current);
        var basis = first - 1;
        var initialKind = TypographyAt(basis);
        var initial = SlotOf(index, basis, initialKind);
        double dx = initial.Dx, dy = initial.Dy, scale = initial.Scale, rotation = initial.Rotation;
        double alpha = initial.Alpha, orient = initial.Orient, wrap = initial.Wrap;
        (LumiereSlot Before, LumiereSlot After, double Phase, double Start, LumiereTypography Kind)? latest = null;
        var result = new LineTransform { RestOrient = initial.Orient, RestWrap = initial.Wrap };
        var widthCap = MaxWidthOf(initialKind);
        var jitterOn = initialKind == LumiereTypography.Crossed ? 0.0 : 1;
        double awayX = StackX(initialKind) * AwayOf(index, basis, initialKind);
        double awayY = (1 - StackX(initialKind)) * AwayOf(index, basis, initialKind);
        double hero = index == basis ? 1 : 0;
        var last = Math.Min(LineCount - 1, current);
        for (var c = first; c <= last; c++)
        {
            var beforeKind = TypographyAt(c - 1);
            var afterKind = TypographyAt(c);
            var before = SlotOf(index, c - 1, beforeKind);
            var after = SlotOf(index, c, afterKind);
            var kindChanged = beforeKind != afterKind;
            if (before == after && !kindChanged) continue;
            var (phase, start) = LinePhase(c, index - c, time);
            var k = EaseInOutSine(phase);
            if (kindChanged)
            {
                widthCap += (MaxWidthOf(afterKind) - MaxWidthOf(beforeKind)) * k;
                jitterOn += ((afterKind == LumiereTypography.Crossed ? 0 : 1) - (beforeKind == LumiereTypography.Crossed ? 0 : 1)) * k;
            }
            var awayBefore = AwayOf(index, c - 1, beforeKind);
            var awayAfter = AwayOf(index, c, afterKind);
            awayX += (StackX(afterKind) * awayAfter - StackX(beforeKind) * awayBefore) * k;
            awayY += ((1 - StackX(afterKind)) * awayAfter - (1 - StackX(beforeKind)) * awayBefore) * k;
            hero += ((index == c ? 1 : 0) - (index == c - 1 ? 1 : 0)) * k;
            // Alpha has its own curve: fading lines leave first (first 60%), appearing ones arrive last (last 60%).
            var fade = after.Alpha < before.Alpha
                ? EaseInOutCubic(phase / 0.6)
                : after.Alpha > before.Alpha ? EaseInOutCubic((phase - 0.4) / 0.6) : k;
            dx += (after.Dx - before.Dx) * k;
            dy += (after.Dy - before.Dy) * k;
            scale += (after.Scale - before.Scale) * k;
            rotation += (after.Rotation - before.Rotation) * k;
            alpha += (after.Alpha - before.Alpha) * fade;
            orient += (after.Orient - before.Orient) * phase;
            wrap += (after.Wrap - before.Wrap) * k;
            if (phase > 0) latest = (before, after, phase, start, afterKind);
            if (phase >= 1)
            {
                result.RestOrient = after.Orient;
                result.RestWrap = after.Wrap;
                result.Moves.Clear();
            }
            else if (phase > 0)
                result.Moves.Add((after.Orient, after.Wrap, phase,
                    _options.AlwaysFly || afterKind == LumiereTypography.Crossed || before.Orient != after.Orient));
        }
        // Too long: shrink as a whole within the frame (width horizontally, length vertically; single / wrapped mixed).
        var o = Clamp01(orient);
        var wr = Clamp01(wrap);
        var hSingle = view.Flow[0][0];
        var hWrapped = view.Flow[0][1];
        var vSingle = view.Flow[1][0];
        var vWrapped = view.Flow[1][1];
        var fitH = Lerp(LumiereLineWrap.FitScale(hSingle.Along, widthCap, scale), LumiereLineWrap.FitScale(hWrapped.Along, widthCap, scale), wr);
        var fitV = Lerp(LumiereLineWrap.FitScale(vSingle.Along, _columnCap, scale), LumiereLineWrap.FitScale(vWrapped.Along, _columnCap, scale), wr);
        var fitted = scale * Lerp(fitH, fitV, o);
        // Stagger only applies to fixed-slot neighbours (across the stacking axis); the current line stays centered.
        var jitter = jitterOn * view.Jitter * _region.W * Clamp01((1 - scale) / 0.5);
        // Fixed-slot lines stay inside the frame along their own direction; crossed neighbours keep their free placement.
        var baseX = _region.Cx + dx + jitter * (1 - orient);
        var baseY = _region.Cy + dy + jitter * 0.5 * orient;
        var alongH = Lerp(hSingle.InkWidth, hWrapped.InkWidth, wr) / _height * fitted;
        var alongV = Lerp(vSingle.InkHeight, vWrapped.InkHeight, wr) / _height * fitted;
        var x = baseX + (LumiereLineWrap.ClampInto(baseX, alongH, _band.Left, _band.Right) - baseX) * (1 - o) * jitterOn;
        var y = baseY + (LumiereLineWrap.ClampInto(baseY, alongV, _band.Top, _band.Bottom) - baseY) * o * jitterOn;
        // Endless drift from the moment the line starts (on its slot while sung) + orbit, sway and breath. Neighbours'
        // drift towards the current line turns away; the current line circles instead of drifting off.
        var age = time - view.Line.StartTime;
        var m = view.MotionPhase;
        var orbitX = Math.Sin(time * 0.52 + m) * Orbit;
        var orbitY = Math.Cos(time * 0.41 + m * 1.3) * Orbit * 0.7;
        var held = Clamp01(hero);
        var driftScale = _options.Drift;
        var driftX = Lerp(AwayDrift((view.VelocityX * age + orbitX) * driftScale, awayX),
            (HeldDrift(view.VelocityX, view.VelocityY, age, 0) + orbitX) * driftScale, held);
        var driftY = Lerp(AwayDrift((view.VelocityY * age + orbitY) * driftScale, awayY),
            (HeldDrift(view.VelocityX, view.VelocityY, age, 1) + orbitY) * driftScale, held);
        var settledOrient = latest?.After.Orient ?? initial.Orient;
        var settledWrap = latest?.After.Wrap ?? initial.Wrap;
        result.Current = current;
        result.X = (x + driftX) * _height;
        result.Y = (y + driftY) * _height;
        result.Scale = fitted * (1 + 0.025 * Math.Sin(time * 0.43 + m));
        result.Rotation = rotation + 0.03 * Math.Sin(time * 0.23 + m * 0.7);
        result.Alpha = Clamp01(alpha);
        result.FromOrient = latest?.Before.Orient ?? settledOrient;
        result.ToOrient = settledOrient;
        result.FromWrap = latest?.Before.Wrap ?? settledWrap;
        result.ToWrap = settledWrap;
        result.Wrap = wr;
        result.Orient = o;
        result.Phase = latest?.Phase ?? 1;
        result.Fly = latest is { } l && l.Phase < 1
            && (_options.AlwaysFly || l.Kind == LumiereTypography.Crossed || l.Before.Orient != l.After.Orient);
        result.SlideStart = latest?.Start ?? double.NegativeInfinity;
        return result;
    }

    private static double FlightProgress(GlyphView glyph, double phase)
    {
        var t = Clamp01((phase - glyph.FlightDelay) / glyph.FlightDuration);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    private static LumierePoint FlightPoint(GlyphView glyph, LumierePoint from, LumierePoint to, double s)
    {
        var u = 1 - s;
        double a = u * u * u, b = 3 * u * u * s, c = 3 * u * s * s, d = s * s * s;
        return new LumierePoint(
            a * from.X + b * (from.X + glyph.C1X) + c * (to.X + glyph.C2X) + d * to.X,
            a * from.Y + b * (from.Y + glyph.C1Y) + c * (to.Y + glyph.C2Y) + d * to.Y);
    }

    private readonly record struct GlyphLocal(double X, double Y, double Rotation, double Gather, double Flying, double Scale);

    private static double DecayAmount(LumiereDecaySpec decay, double litAt, double time)
    {
        var age = time - litAt - decay.Delay;
        return age > 0 ? decay.Strength * 0.08 * Math.Pow(age, 1.5) : 0;
    }

    /// <summary>A glyph's position in its line (unscaled), rotation and scale: flying along its Bézier on slot changes,
    /// then gather, decay and breath.</summary>
    private GlyphLocal Local(LineView view, GlyphView glyph, double time, LineTransform transform)
    {
        LumierePoint At(double orient, double wrap) => view.Flow[orient >= 0.5 ? 1 : 0][wrap >= 0.5 ? 1 : 0].Points[glyph.Index];
        // Start at the last finished move's end, then stack moves still running: each takes off from where the previous
        // one is now, so a line change mid-flight never snaps back.
        var point = At(transform.RestOrient, transform.RestWrap);
        var orientation = transform.RestOrient;
        var flying = 0.0;
        foreach (var move in transform.Moves)
        {
            var target = At(move.ToOrient, move.ToWrap);
            if (move.Fly)
            {
                var s = FlightProgress(glyph, move.Phase);
                point = FlightPoint(glyph, point, target, s);
                orientation = Lerp(orientation, move.ToOrient, s);
                flying = Math.Max(flying, Math.Sin(Math.PI * s));
            }
            else
            {
                var k = EaseInOutSine(move.Phase);
                point = new LumierePoint(Lerp(point.X, target.X, k), Lerp(point.Y, target.Y, k));
                orientation = Lerp(orientation, move.ToOrient, k);
            }
        }
        var decay = _options.Decay;
        var x = point.X;
        var y = point.Y;
        var rotation = glyph.VRotation * orientation + glyph.FlightSpin * flying;
        var pulse = 1 - 0.28 * flying;
        // Gather: unsung lines close in from scattered positions.
        var gather = Smooth((time - (view.Line.StartTime - Lead - GatherLead)) / Gather);
        var scatter = Math.Pow(1 - gather, 2) * decay.Strength;
        x += glyph.ScatterX * _heroPx * scatter;
        y += glyph.ScatterY * _heroPx * scatter;
        // Decay: a while after lighting, drift off along its own direction and turn.
        var amount = DecayAmount(decay, glyph.Timing.Start, time) * glyph.DriftSpeed;
        x += glyph.DriftDx * _heroPx * amount;
        y += glyph.DriftDy * _heroPx * amount;
        rotation += glyph.DriftSpin * amount * 0.18;
        // Breath: always a slight sway.
        var breath = _heroPx * 0.022 * Math.Min(1, decay.Strength + 0.3);
        x += Math.Sin(time * 0.9 + glyph.Phase) * breath;
        y += Math.Cos(time * 1.13 + glyph.Phase * 1.7) * breath;
        return new GlyphLocal(x, y, rotation, gather, flying, glyph.Scale * pulse);
    }

    private static LumierePoint ToWorld(LineTransform transform, double x, double y)
    {
        var cos = Math.Cos(transform.Rotation);
        var sin = Math.Sin(transform.Rotation);
        return new LumierePoint(transform.X + (x * cos - y * sin) * transform.Scale, transform.Y + (x * sin + y * cos) * transform.Scale);
    }

    // ---- protection -------------------------------------------------------------------------------------------

    /// <summary>
    /// This frame's protect boxes: line j's weight as current = its change's eased progress − the next change's (continuous,
    /// sums to 1). The box is j's ink box now, following its position, scale and rotation; it fades while a line turns.
    /// </summary>
    private void BuildProtectBoxes(double time, int current, int low)
    {
        _protectCount = 0;
        var later = 0.0;
        for (var j = current; j >= low && _protectCount < _protectBoxes.Length; j--)
        {
            var eased = EaseInOutSine((time - (_metas[j].Line.StartTime - Lead)) / Slide);
            var transform = _frameTransforms[j]!;
            var o = Clamp01(transform.Orient);
            var weight = (eased - later) * Clamp01(transform.Alpha) * (1 - 4 * o * (1 - o));
            later = eased;
            if (weight > 1e-4)
            {
                var flow = LineOf(j).Flow;
                var w = transform.Wrap;
                var inkW = Lerp(Lerp(flow[0][0].InkWidth, flow[0][1].InkWidth, w), Lerp(flow[1][0].InkWidth, flow[1][1].InkWidth, w), transform.Orient);
                var inkH = Lerp(Lerp(flow[0][0].InkHeight, flow[0][1].InkHeight, w), Lerp(flow[1][0].InkHeight, flow[1][1].InkHeight, w), transform.Orient);
                var box = _protectBoxes[_protectCount++];
                box.Line = j;
                box.X = transform.X;
                box.Y = transform.Y;
                box.Cos = Math.Cos(transform.Rotation);
                box.Sin = Math.Sin(transform.Rotation);
                box.HalfW = inkW / 2 * transform.Scale;
                box.HalfH = inkH / 2 * transform.Scale;
                box.Margin = ProtectMargin * _heroPx * transform.Scale;
                box.Weight = weight;
            }
            if (eased >= 1) break;
        }
    }

    /// <summary>How protected a glyph (center x, y, half size) of line <paramref name="line"/> is by other lines' boxes.</summary>
    private double ProtectionAt(int line, double x, double y, double half)
    {
        var protect = 0.0;
        for (var i = 0; i < _protectCount; i++)
        {
            var box = _protectBoxes[i];
            if (box.Line == line || box.Weight <= 0) continue;
            var dx = x - box.X;
            var dy = y - box.Y;
            var u = Math.Abs(dx * box.Cos + dy * box.Sin) - box.HalfW - half;
            var v = Math.Abs(dy * box.Cos - dx * box.Sin) - box.HalfH - half;
            var outside = u > 0 && v > 0 ? Math.Sqrt(u * u + v * v) : Math.Max(Math.Max(u, v), 0);
            protect += box.Weight * (1 - Smooth(outside / box.Margin));
        }
        return Math.Min(1, protect);
    }

    private double ProtectedAlpha(double protect) => 1 - (1 - ProtectFloor) * protect;

    // ---- window range -----------------------------------------------------------------------------------------

    private (int Current, int Low, int High) ActiveRange(double time)
    {
        var current = Cursor(time);
        var first = FirstLiveChange(time, current);
        return (current, Math.Max(0, Math.Min(current, first) - WindowReach), Math.Min(LineCount - 1, Math.Max(current, 0) + WindowReach));
    }

    private void SyncBuilt(int low, int high)
    {
        for (var k = _built.Count - 1; k >= 0; k--)
        {
            var index = _built[k];
            if (index >= low - KeepMargin && index <= high + KeepMargin) continue;
            Release(index);
        }
    }

    private void PrebuildAhead(int high)
    {
        for (var index = high + 1; index <= Math.Min(LineCount - 1, high + Prebuild); index++)
        {
            if (_views[index] is not null) continue;
            Build(index);
            return;
        }
    }

    // ---- spot -------------------------------------------------------------------------------------------------

    private static double SingingPosition(GlyphView[] glyphs, double time)
    {
        var position = 0.0;
        foreach (var glyph in glyphs) position += LumiereReveal.Progress(glyph.Timing, time);
        return position;
    }

    private static double PositionToCoordinate(LumierePoint[] path, GlyphView[] glyphs, double position, bool y)
    {
        var n = glyphs.Length;
        if (n == 0) return 0;
        var index = Math.Min(n - 1, Math.Max(0, position - 0.5));
        var lower = (int)Math.Floor(index);
        var upper = Math.Min(n - 1, lower + 1);
        double At(int i) => y ? path[glyphs[i].Index].Y : path[glyphs[i].Index].X;
        return At(lower) + (At(upper) - At(lower)) * (index - lower);
    }

    /// <summary>The follow spot along the line, averaged over the past SPOT_WINDOW seconds (still pure in t).</summary>
    private static LumierePoint SpotAt(LumierePoint[] path, GlyphView[] glyphs, double time)
    {
        double x = 0, y = 0;
        for (var s = 0; s < SpotSamples; s++)
        {
            var position = SingingPosition(glyphs, time - SpotWindow * s / (SpotSamples - 1));
            x += PositionToCoordinate(path, glyphs, position, false);
            y += PositionToCoordinate(path, glyphs, position, true);
        }
        return new LumierePoint(x / SpotSamples, y / SpotSamples);
    }

    // ---- public -----------------------------------------------------------------------------------------------

    /// <summary>Each visible glyph's lighting time, for burst planning.</summary>
    public IReadOnlyList<LumiereBurstGlyph> GlyphTimes(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= LineCount) return [];
        var meta = _metas[lineIndex];
        var output = new List<LumiereBurstGlyph>();
        for (var i = 0; i < meta.Graphemes.Count; i++)
            if (!string.IsNullOrWhiteSpace(meta.Graphemes[i]))
                output.Add(new LumiereBurstGlyph(i, i < meta.Timings.Count ? meta.Timings[i].Start : meta.Line.StartTime));
        return output;
    }

    /// <summary>A glyph's position (stage logical px, with decay) and font size at <paramref name="time"/>.</summary>
    public (float X, float Y, float FontPx) GlyphAnchor(int lineIndex, int glyphIndex, double time)
    {
        var transform = Transform(lineIndex, time);
        var view = LineOf(lineIndex);
        var local = Local(view, view.Glyphs[glyphIndex], time, transform);
        var world = ToWorld(transform, local.X, local.Y);
        return ((float)world.X, (float)world.Y, (float)(_heroPx * transform.Scale * local.Scale));
    }

    /// <summary>A line's center (logical px), scale and alpha at <paramref name="time"/> (debugging, continuity checks).</summary>
    public (double X, double Y, double Scale, double Alpha) LineAnchor(int lineIndex, double time)
    {
        var transform = Transform(lineIndex, time);
        return (transform.X, transform.Y, transform.Scale, transform.Alpha);
    }

    private readonly record struct GlyphFrame(
        bool Visible, GlyphLocal Local, double Gx, double Gy, double FontPx,
        double Alpha, EffectColor Tint, double HaloAlpha, double HaloSize,
        double StarAlpha, double StarX, double StarY, double StarRotation, double StarSize,
        EffectColor? HaloTint = null, EffectColor? StarTint = null);

    private Vector3?[] KeywordColorsOf(int lineIndex)
    {
        var meta = _metas[lineIndex];
        return meta.KeywordColors ??= _options.Keywords.Count > 0
            ? LumiereKeywords.GlyphColors(meta.Line.Text, _options.Keywords)
            : new Vector3?[meta.Graphemes.Count];
    }

    /// <summary>A glyph's keyword color (null when it is not one), for cross bursts.</summary>
    public Vector3? GlyphKeyword(int lineIndex, int glyphIndex)
    {
        if (lineIndex < 0 || lineIndex >= LineCount || _options.Keywords.Count == 0) return null;
        var colors = KeywordColorsOf(lineIndex);
        return glyphIndex >= 0 && glyphIndex < colors.Length ? colors[glyphIndex] : null;
    }

    // Keyword tints depend only on the light color (usually constant for the unit): computed once per color.
    private readonly Dictionary<Vector3, LumiereKeywordTints> _tints = [];
    private Vector3 _tintsFor = new(float.NaN);

    private LumiereKeywordTints TintsOf(Vector3 litColor, Vector3 keyword)
    {
        if (_tintsFor != litColor)
        {
            _tints.Clear();
            _tintsFor = litColor;
        }
        if (!_tints.TryGetValue(keyword, out var tints)) _tints[keyword] = tints = LumiereKeywords.Tints(litColor, keyword);
        return tints;
    }

    private readonly List<(LineView View, LineTransform Transform, GlyphFrame[] Glyphs, double SpotAlpha,
        LumierePoint Spot, double SpotSize, bool SpotVertical, double TrackAlpha)> _frame = [];

    /// <summary>Draws trails, halos, glyphs, sparkles and spots (Folia's layer order) into the current target.</summary>
    /// <param name="root">Stage logical px → target pixels.</param>
    public void Draw(EffectPrimitiveRenderer primitives, LumiereSprites sprites, Matrix3x2 root, in LumiereWindowFrame frame)
    {
        var time = frame.Time;
        var beams = frame.Beams;
        var litColor = frame.LitColor;
        var litTint = LumiereColor.Tint(litColor);
        var starTint = LumiereColor.Tint(LumiereColor.Mix(litColor, LumiereColor.White, 0.5f));
        var (cursor, low, high) = ActiveRange(time);
        SyncBuilt(low, high);
        if (LineCount == 0) return;
        for (var index = low; index <= high; index++) _frameTransforms[index] = Transform(index, time);
        BuildProtectBoxes(time, cursor, low);

        _frame.Clear();
        for (var index = low; index <= high; index++)
        {
            var view = LineOf(index);
            var transform = _frameTransforms[index]!;
            var lineAlpha = transform.Alpha * frame.Intensity;
            var visible = lineAlpha > 0.003;
            var passed = index < transform.Current || index == transform.Current && time > view.Line.EndTime;
            var passedDim = passed ? Lerp(1, 0.75, Clamp01((time - view.Line.EndTime) / 2.5)) : 1;
            var lineFontPx = _heroPx * transform.Scale;
            var glyphs = new GlyphFrame[view.Glyphs.Length];
            if (visible)
            {
                for (var g = 0; g < view.Glyphs.Length; g++)
                {
                    var glyph = view.Glyphs[g];
                    if (glyph.Blank) continue;
                    var local = Local(view, glyph, time, transform);
                    var fontPx = lineFontPx * local.Scale;
                    var world = ToWorld(transform, local.X, local.Y);
                    var lit = Smooth((time - glyph.Timing.Start) / Math.Max(glyph.Timing.End - glyph.Timing.Start, LightUp));
                    var flash = LumiereReveal.Flash(glyph.Timing, time, 0.45);
                    var illumination = LumiereLight.CompressLight(LumiereLight.LightAt(beams, world.X / _height, world.Y / _height));
                    // The sparkle carries the flash; the body only lifts a little (near-white strokes blur under the bloom).
                    var heat = Clamp01(illumination * 1.3 + flash * 0.2);
                    // The farther a glyph decays, the fainter: dissolving into the smoke.
                    var dissolve = 1 - Clamp01((DecayAmount(_options.Decay, glyph.Timing.Start, time) * glyph.DriftSpeed - DissolveFrom) / DissolveSpan);
                    // Inside the current line's protect box everything dims; flying glyphs are spared (they flash through).
                    var shield = _protectCount > 0
                        ? ProtectedAlpha(ProtectionAt(index, world.X, world.Y, fontPx / 2) * (1 - local.Flying))
                        : 1;
                    var glyphAlpha = lineAlpha * passedDim * dissolve * (0.35 + 0.65 * local.Gather) * shield;
                    // Unsung glyphs are revealed by the beams (cool, half bright); sung ones warm gold; flying ones glow.
                    var revealed = Math.Min(1, frame.UnlitAlpha + illumination * 0.45 + local.Flying * 0.4);
                    var alpha = glyphAlpha * (revealed * (1 - lit) + lit * Math.Min(1, 0.55 + 0.4 * heat + 0.3 * local.Flying));
                    // Keywords: once lit, body, halo and sparkle take the keyword light (unsung glyphs stay cool).
                    var keyword = _options.Keywords.Count > 0 ? KeywordColorsOf(index)[glyph.Index] : null;
                    var tints = keyword is { } k ? TintsOf(litColor, k) : null;
                    var hot = LumiereColor.Mix(tints?.Glyph ?? litColor, LumiereColor.White, (float)Clamp01(0.08 * illumination + 0.1 * flash + 0.25 * local.Flying));
                    var cold = LumiereColor.Mix(frame.UnlitColor, litColor, (float)(illumination * 0.35));
                    var tint = LumiereColor.Tint(LumiereColor.Mix(cold, hot, (float)lit));
                    // The bloom is strong already: halo and sparkle only add "locally brighter", never a white blob.
                    var haloAlpha = glyphAlpha * lit * (0.03 + 0.14 * illumination + 0.08 * flash) * (tints is null ? 1 : LumiereKeywords.HaloGain);
                    var starAlpha = lineAlpha * flash * 0.55 * shield;
                    glyphs[g] = new GlyphFrame(true, local, world.X, world.Y, fontPx, alpha, tint,
                        haloAlpha, fontPx * (2.2 + 0.8 * illumination),
                        starAlpha, world.X + fontPx * glyph.StarDx, world.Y + fontPx * glyph.StarDy,
                        glyph.StarRotation * (1 + (1 - flash) * 0.6), fontPx * glyph.StarSize * (0.8 + 0.5 * flash),
                        tints is null ? null : LumiereColor.Tint(tints.Halo), tints is null ? null : LumiereColor.Tint(tints.Star));
                }
            }
            // Follow spot: moves continuously along the line, time-averaged; fades in 0.3 s before singing, out 0.6 s after.
            var spotAlpha = visible
                ? lineAlpha * Smooth((time - view.SingStart + 0.3) / 0.3) * (1 - Smooth((time - view.SingEnd) / 0.6))
                : 0;
            var vertical = transform.ToOrient >= 0.5;
            var spot = default(LumierePoint);
            if (spotAlpha > 0.003)
            {
                var flow = view.Flow[vertical ? 1 : 0];
                var local = transform.Wrap <= 0 ? SpotAt(flow[0].Spots, view.Glyphs, time)
                    : transform.Wrap >= 1 ? SpotAt(flow[1].Spots, view.Glyphs, time)
                    : Mix(SpotAt(flow[0].Spots, view.Glyphs, time), SpotAt(flow[1].Spots, view.Glyphs, time), transform.Wrap);
                spot = ToWorld(transform, local.X, local.Y);
            }
            _frame.Add((view, transform, glyphs, spotAlpha, spot, lineFontPx * 5, vertical, lineAlpha * passedDim));
        }

        // Spots (bottom), trails, halos, glyphs, sparkles — each layer in line order.
        foreach (var line in _frame)
        {
            if (line.SpotAlpha <= 0.003) continue;
            LumiereDraw.Sprite(primitives, sprites.Dot, root, (float)line.Spot.X, (float)line.Spot.Y,
                (float)(line.SpotSize * (line.SpotVertical ? 1 : 1.6)), (float)(line.SpotSize * (line.SpotVertical ? 1.6 : 1)),
                (float)line.Transform.Rotation, (float)(0.07 * line.SpotAlpha), litTint);
        }
        if (!frame.HideTrails)
            foreach (var line in _frame)
                DrawTracks(primitives, root, line.View, time, line.Transform, line.TrackAlpha, litColor);
        foreach (var line in _frame)
            foreach (var g in line.Glyphs)
            {
                if (!g.Visible || g.HaloAlpha <= 0.003) continue;
                LumiereDraw.Sprite(primitives, sprites.Dot, root, (float)g.Gx, (float)g.Gy, (float)g.HaloSize,
                    (float)(g.HaloSize * 0.9), 0, (float)g.HaloAlpha, g.HaloTint ?? litTint);
            }
        foreach (var line in _frame)
        {
            var t = line.Transform;
            var holder = LumiereDraw.Container((float)t.X, (float)t.Y, (float)t.Rotation, (float)t.Scale, (float)t.Scale) * root;
            var texture = line.View.Layout.Texture;
            for (var i = 0; i < line.Glyphs.Length; i++)
            {
                var g = line.Glyphs[i];
                if (!g.Visible || g.Alpha <= 0) continue;
                var slice = line.View.Glyphs[i].Slice;
                var glyphScale = (float)(g.Local.Scale / LumiereWordStyle.MaxWordScale);
                var transform = Matrix3x2.CreateTranslation(-slice.Size.X * slice.AnchorX, -slice.Size.Y * slice.AnchorY)
                    * Matrix3x2.CreateScale(glyphScale)
                    * Matrix3x2.CreateRotation((float)g.Local.Rotation)
                    * Matrix3x2.CreateTranslation((float)g.Local.X, (float)g.Local.Y) * holder;
                primitives.DrawTextureRegion(texture, transform, slice.Size, slice.Uv, (float)g.Alpha, g.Tint);
            }
        }
        foreach (var line in _frame)
            foreach (var g in line.Glyphs)
            {
                if (!g.Visible || g.StarAlpha <= 0.003) continue;
                LumiereDraw.Sprite(primitives, sprites.Star, root, (float)g.StarX, (float)g.StarY, (float)g.StarSize,
                    (float)g.StarSize, (float)g.StarRotation, (float)g.StarAlpha, g.StarTint ?? starTint);
            }
        PrebuildAhead(high);
    }

    private static LumierePoint Mix(LumierePoint a, LumierePoint b, double k) => new(Lerp(a.X, b.X, k), Lerp(a.Y, b.Y, k));

    /// <summary>
    /// Trails: the path each glyph really took over the past TRACK_TIME seconds (line motion, rotation and scale included)
    /// with a slight wobble, like particle tracks in a cloud chamber; once landed the tail catches up and the trail closes.
    /// </summary>
    private void DrawTracks(EffectPrimitiveRenderer primitives, Matrix3x2 root, LineView view, double time,
        LineTransform transform, double alpha, Vector3 color)
    {
        if (alpha <= 0.003 || !double.IsFinite(transform.SlideStart)) return;
        if (time > transform.SlideStart + Slide + TrackTime) return;
        var from = Math.Max(transform.SlideStart, time - TrackTime);
        if (time - from < 1e-3) return;
        var samples = new double[TrackSamples + 1];
        var transforms = new LineTransform[TrackSamples + 1];
        var anyFly = false;
        for (var i = 0; i <= TrackSamples; i++)
        {
            samples[i] = from + (time - from) * i / TrackSamples;
            transforms[i] = Transform(view.Index, samples[i]);
            anyFly |= transforms[i].Fly;
        }
        if (!anyFly) return;
        var rootScale = MathF.Sqrt(MathF.Abs(root.GetDeterminant()));
        var width = 1.2f * rootScale;
        foreach (var glyph in view.Glyphs)
        {
            if (glyph.Blank) continue;
            _trackPoints.Clear();
            var length = 0.0;
            LumierePoint? previous = null;
            var fontPx = _heroPx;
            var flying = 0.0;
            for (var i = 0; i <= TrackSamples; i++)
            {
                var local = Local(view, glyph, samples[i], transforms[i]);
                var world = ToWorld(transforms[i], local.X, local.Y);
                fontPx = _heroPx * transforms[i].Scale * local.Scale;
                flying = local.Flying;
                var wobble = Math.Sin(i * 0.9 + glyph.Wobble + samples[i] * 6) * _heroPx * 0.02;
                var point = new LumierePoint(world.X + wobble, world.Y - wobble * 0.6);
                if (previous is { } p) length += Math.Sqrt((point.X - p.X) * (point.X - p.X) + (point.Y - p.Y) * (point.Y - p.Y));
                previous = point;
                _trackPoints.Add(Vector2.Transform(new Vector2((float)point.X, (float)point.Y), root));
            }
            // Longer (faster) trails are brighter; nearly still glyphs leave none. A head inside the current line's box dims.
            var strength = Math.Min(1, length / (_heroPx * 3));
            if (strength <= 0.05) continue;
            var head = previous!.Value;
            var shield = _protectCount > 0
                ? ProtectedAlpha(ProtectionAt(view.Index, head.X, head.Y, fontPx / 2) * (1 - flying))
                : 1;
            var a = (float)(alpha * 0.55 * strength * shield);
            if (a <= 0.002) continue;
            _track.Points = _trackPoints;
            _track.StartPointIndex = 0;
            _track.EndPointIndex = int.MaxValue;
            _track.TailWidth = _track.HeadWidth = width;
            _track.Color = new EffectColor(color.X, color.Y, color.Z, a);
            primitives.DrawPolyline(_track);
        }
    }

    public void Dispose()
    {
        for (var k = _built.Count - 1; k >= 0; k--) Release(_built[k]);
        _measurer.Dispose();
    }
}
