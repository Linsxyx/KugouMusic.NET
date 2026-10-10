using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Lumiere.Text;

/// <summary>
/// Folia text/lyricEcho.ts: background decoration made of "collected" words. Each word, when sung, appears as a
/// huge hollow fragment near the main source and drifts down the main beam (following its sway), fading in ~7 s;
/// size, tilt, stretch and shear are seeded, so fragments overlap in the beam. While drifting the word comes apart:
/// glyphs spread, turn and shear. Faint by default, lit up inside beams. Each frame depends on t only.
/// All random draws happen up front (same order as drawing everything at once); hollow glyphs are rasterized only
/// PREPARE seconds before a line's first fragment (at most one line ahead per frame) and released after the last.
/// </summary>
internal sealed class LumiereEcho : IDisposable
{
    private const double Life = 7;
    private const double Speed = 0.075;
    private const double BirthDistance = 0.08;
    private const double Prepare = 3;

    private readonly GL _gl;
    private readonly float _width;
    private readonly float _height;
    private readonly float _fontPx;
    private readonly float _resolution;
    private readonly double _opacity;
    private readonly string _font;
    private readonly int _weight;
    private readonly List<Fragment> _fragments = [];
    private readonly List<EchoLine> _lines = [];

    private sealed class FragmentGlyph
    {
        public bool Blank;
        public LumiereGlyphSlice? Slice;
        public float Offset;
        public double Dx, Dy, Speed, Spin;
        public System.Numerics.Vector3? Keyword;
    }

    private sealed class Fragment
    {
        public double Birth, Across, Speed, Scale, Rotation, Stretch, Shear, Spin;
        public int Start, End;
        public FragmentGlyph[] Glyphs = [];
        public EchoLine Line = null!;
    }

    private sealed class EchoLine
    {
        public required string Text;
        public int First, Last;
        public double From, To;
        public LumiereGlyphLine? Layout;
    }

    public LumiereEcho(GL gl, IReadOnlyList<LumiereLine> lines, float width, float height, string font, int weight,
        float resolution, string seed, double size, double opacity, IReadOnlyList<LumiereKeywordMatcher> keywords)
    {
        _gl = gl;
        _width = width;
        _height = height;
        _fontPx = (float)(size * height);
        // Background glyphs are faint; they need no high resolution.
        _resolution = MathF.Min(resolution, 1.5f);
        _opacity = opacity;
        _font = font;
        _weight = weight;
        var rng = new LumiereRng($"{seed}:echo");
        foreach (var line in lines)
        {
            var graphemes = LumiereReveal.Graphemes(line.Text);
            var keywordColors = keywords.Count > 0 ? LumiereKeywords.GlyphColors(line.Text, keywords) : null;
            var timings = LumiereReveal.Timeline(line).Timings;
            var first = _fragments.Count;
            var echoLine = new EchoLine { Text = line.Text };
            foreach (var word in LumiereWords.Segment(line.Text))
            {
                if (word.Blank) continue;
                var end = Math.Min(word.End, graphemes.Count);
                if (end <= word.Start) continue;
                var fragment = new Fragment
                {
                    Line = echoLine,
                    Birth = word.Start < timings.Count ? timings[word.Start].Start : line.StartTime,
                    Across = (rng.Next() - 0.5) * 2.8,
                    Speed = 0.7 + rng.Next() * 0.7,
                    Scale = 0.4 + rng.Next() * 0.6,
                    Rotation = (rng.Next() - 0.5) * 0.9,
                    Stretch = 0.75 + rng.Next() * 0.7,
                    Shear = (rng.Next() - 0.5) * 0.12,
                    Spin = (rng.Next() - 0.5) * 0.08,
                    Start = word.Start,
                    End = end,
                };
                fragment.Glyphs = new FragmentGlyph[end - word.Start];
                for (var index = 0; index < fragment.Glyphs.Length; index++)
                {
                    var angle = rng.Next() * Math.PI * 2;
                    fragment.Glyphs[index] = new FragmentGlyph
                    {
                        Blank = string.IsNullOrWhiteSpace(graphemes[word.Start + index]),
                        Dx = Math.Cos(angle),
                        Dy = Math.Sin(angle),
                        Speed = 0.3 + rng.Next() * 0.9,
                        Spin = (rng.Next() - 0.5) * 0.5,
                        Keyword = keywordColors?[word.Start + index],
                    };
                }
                _fragments.Add(fragment);
            }
            if (_fragments.Count == first) continue;
            echoLine.First = first;
            echoLine.Last = _fragments.Count;
            echoLine.From = _fragments.Skip(first).Min(fragment => fragment.Birth);
            echoLine.To = _fragments.Skip(first).Max(fragment => fragment.Birth);
            _lines.Add(echoLine);
        }
    }

    private void Rasterize(EchoLine line)
    {
        var layout = new LumiereGlyphLine(_gl, line.Text, _fontPx, _font, _weight, _resolution,
            letterSpacing: 0.04f, outline: 0.012f, maxCanvasPx: 4096);
        line.Layout = layout;
        for (var index = line.First; index < line.Last; index++)
        {
            var fragment = _fragments[index];
            var left = layout.Glyphs[fragment.Start].CharX;
            var last = layout.Glyphs[fragment.End - 1];
            var middle = (left + last.CharX + last.CharWidth) / 2;
            for (var offset = 0; offset < fragment.Glyphs.Length; offset++)
            {
                var slice = layout.Glyphs[fragment.Start + offset];
                fragment.Glyphs[offset].Slice = slice;
                fragment.Glyphs[offset].Offset = slice.CharX + slice.CharWidth / 2 - middle;
            }
        }
    }

    private void Release(EchoLine line)
    {
        for (var index = line.First; index < line.Last; index++)
            foreach (var glyph in _fragments[index].Glyphs) glyph.Slice = null;
        line.Layout?.Dispose();
        line.Layout = null;
    }

    /// <summary>Lines with fragments already out rasterize now; upcoming ones at most one per frame; finished ones release.</summary>
    private void PrepareLines(double time)
    {
        var budget = 1;
        foreach (var line in _lines)
        {
            if (time < line.From - Prepare || time > line.To + Life)
            {
                if (line.Layout is not null) Release(line);
            }
            else if (line.Layout is null && (time >= line.From || budget-- > 0))
                Rasterize(line);
        }
    }

    private static double Smooth(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    public void Draw(EffectPrimitiveRenderer primitives, Matrix3x2 root, double time,
        IReadOnlyList<LumiereResolvedBeam> beams, Vector3 color, double intensity)
    {
        PrepareLines(time);
        // Main beam geometry (the first beam); without beams fall back to straight down from the top center.
        var hasBeam = beams.Count > 0;
        var beam = hasBeam ? beams[0] : default;
        var ox = hasBeam ? beam.Ox : _width / _height / 2.0;
        var oy = hasBeam ? beam.Oy : 0;
        var bx = hasBeam ? beam.Dx : 0;
        var by = hasBeam ? beam.Dy : 1;
        double HalfAt(double distance) => (hasBeam ? beam.HalfWidth + distance * beam.TanSpread : 0.08) + 0.04;
        var tint = LumiereColor.Tint(color);

        foreach (var fragment in _fragments)
        {
            var age = time - fragment.Birth;
            var presence = Smooth(age / 0.9) * (1 - Smooth((age - (Life - 2.5)) / 2.5));
            var alpha = presence * intensity * _opacity;
            if (age < 0 || age > Life || alpha <= 0.003) continue;
            var layout = fragment.Line.Layout;
            if (layout is null) continue;
            // Drift along the beam; the across offset scales with the beam's half width there (follows its sway).
            var distance = BirthDistance + Speed * fragment.Speed * age;
            var across = fragment.Across * HalfAt(distance);
            var x = (ox + bx * distance - by * across) * _height;
            var y = (oy + by * distance + bx * across) * _height;
            var holder = LumiereDraw.Container((float)x, (float)y, (float)(fragment.Rotation + fragment.Spin * age),
                (float)(fragment.Scale * fragment.Stretch), (float)fragment.Scale, (float)(fragment.Shear * age)) * root;
            // Coming apart: glyphs spread (accelerating as age^1.3) and each turns.
            var split = 0.05 * Math.Pow(age, 1.3);
            var lit = LumiereLight.CompressLight(LumiereLight.LightAt(beams, x / _height, y / _height));
            var glyphAlpha = (float)(alpha * (0.05 + 0.4 * lit));
            foreach (var glyph in fragment.Glyphs)
            {
                if (glyph.Blank || glyph.Slice is not { } slice) continue;
                var gx = (float)(glyph.Offset + glyph.Dx * _fontPx * split * glyph.Speed);
                var gy = (float)(glyph.Dy * _fontPx * split * glyph.Speed);
                var transform = Matrix3x2.CreateTranslation(-slice.Size.X * slice.AnchorX, -slice.Size.Y * slice.AnchorY)
                    * Matrix3x2.CreateRotation((float)(glyph.Spin * split * 4))
                    * Matrix3x2.CreateTranslation(gx, gy) * holder;
                primitives.DrawTextureRegion(layout.Texture, transform, slice.Size, slice.Uv, glyphAlpha,
                    glyph.Keyword is { } keyword ? LumiereColor.Tint(LumiereKeywords.EchoColor(color, keyword)) : tint);
            }
        }
    }

    public void Dispose()
    {
        foreach (var line in _lines) Release(line);
    }
}
