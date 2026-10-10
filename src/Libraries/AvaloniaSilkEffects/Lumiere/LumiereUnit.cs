using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.LineArt;
using AvaloniaSilkEffects.Lumiere.Text;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Lumiere;

/// <summary>One shot of a unit: a profile, its time span and which lines (indices into the unit's lines) it covers.</summary>
public sealed record LumiereShot(LumiereProfile Profile, double StartTime, double EndTime, IReadOnlyList<int> Lines);

/// <summary>Audio features 0..1: bass lifts beams, treble makes motes twinkle, power thickens fog.</summary>
public readonly record struct LumiereAudioFrame(double Bass, double Treble, double Power);

public sealed class LumiereUnitOptions
{
    public required string Seed { get; init; }
    public required LumiereTheme Theme { get; init; }
    public LumiereSceneTuning Tuning { get; init; } = new();
    public required IReadOnlyList<LumiereLine> Lines { get; init; }
    public required IReadOnlyList<LumiereShot> Shots { get; init; }
    public required double StartTime { get; init; }
    public required double EndTime { get; init; }
    /// <summary>The unit opens its paragraph: the starfall opening plays here only.</summary>
    public bool Opening { get; init; }
    /// <summary>Lights-out window: light, line art and text go dark inside it.</summary>
    public (double Start, double End)? FadeOut { get; init; }
    public IReadOnlyList<LumiereSection>? Sections { get; init; }
    /// <summary>One typography for the whole unit (otherwise each shot's own).</summary>
    public LumiereTypography? Typography { get; init; }

    /// <summary>
    /// Folia lumiereUnit.ts: a compiled paragraph as unit options — shot lines become unit-local indices, lights-out becomes
    /// the scene's fade-out window, seeded by program seed and paragraph id.
    /// </summary>
    public static LumiereUnitOptions FromParagraph(LumiereProgram program, LumiereParagraph paragraph, LumiereTheme theme,
        LumiereSceneTuning tuning)
    {
        var local = paragraph.LineIndices.Select((lineIndex, index) => (lineIndex, index)).ToDictionary(item => item.lineIndex, item => item.index);
        var shots = paragraph.Shots.Select(shot => new LumiereShot(Rigs.LumiereCatalog.ProfileOf(shot.Kind), shot.StartTime, shot.EndTime,
            [.. shot.LineIndices.Where(local.ContainsKey).Select(index => local[index])])).ToList();
        // The compiler guarantees a shot per paragraph; defensively cover the paragraph with 天井.
        if (shots.Count == 0) shots.Add(new LumiereShot(Rigs.LumiereCatalog.ProfileOf(""), paragraph.StartTime, paragraph.EndTime, []));
        return new LumiereUnitOptions
        {
            Seed = $"{program.Seed}:{paragraph.Id}",
            Theme = theme,
            Tuning = tuning,
            Lines = paragraph.Lines,
            Shots = shots,
            StartTime = paragraph.StartTime,
            EndTime = paragraph.EndTime,
            Opening = paragraph.Opening,
            FadeOut = paragraph.TransitionOut is { Kind: LumiereTransitionKind.LightsOut } fade ? (fade.StartTime, fade.EndTime) : null,
            Sections = paragraph.Sections,
        };
    }
}

/// <summary>GPU objects shared by every unit of a scene.</summary>
internal sealed class LumiereGpu(GL gl, EffectTextureCache textures) : IDisposable
{
    public GL Gl { get; } = gl;
    public EffectTextureCache Textures { get; } = textures;
    public LumiereLightField LightField { get; } = new(gl);
    public LumiereBloom GraphicsBloom { get; } = new(gl);
    public LumiereBloom TextBloom { get; } = new(gl);
    public LumiereSprites Sprites { get; } = new(gl);
    public LumiereRenderTarget GraphicsTarget { get; } = new(gl);
    public LumiereRenderTarget TextTarget { get; } = new(gl);

    public void Dispose()
    {
        LightField.Dispose();
        GraphicsBloom.Dispose();
        TextBloom.Dispose();
        Sprites.Dispose();
        GraphicsTarget.Dispose();
        TextTarget.Dispose();
    }
}

/// <summary>
/// Folia lumiere/scene.ts: one unit (a paragraph's run of shots). Layer stack, bottom to top:
///   graphics group (bloom): light field → echo → starfall → line art → icons → motes
///   mid (asset insert, empty)
///   text group (bloom): cross bursts → lyric window (trails, halos, glyphs, sparkles)
///   front: bokeh
/// All of it sits on a stage carrying the camera; the dark field is drawn by the scene under every unit.
/// Each frame depends on t only. Shots hand their rigs over by cross-fading two beam sets in one light field.
/// </summary>
internal sealed class LumiereUnit : IDisposable
{
    /// <summary>Seconds over which shot rigs cross-fade.</summary>
    private const double Handoff = 0.9;
    /// <summary>Seconds the source glare takes to move to the new rig's position (cubic in-out).</summary>
    private const double GlareMove = 1.6;

    private readonly LumiereUnitOptions _options;
    private readonly float _width;
    private readonly float _height;
    private readonly double _aspect;
    private readonly float _overscan;
    private readonly LumierePalette _palette;
    private readonly LumiereShot[] _shots;
    private readonly LumiereLightRig[] _rigs;
    private readonly LumiereProfile _lead;
    private readonly LumiereCamera _camera;
    private readonly LumiereMotes _motes;
    private readonly LumiereMotes? _front;
    private readonly bool _opening;
    private readonly double _ignition;
    private readonly LumiereLyricWindow _lyrics;
    private readonly LumiereStarfall? _starfall;
    private readonly LumiereLineArtLayer[] _lineArts;
    private readonly double _starOffset;
    private readonly LumiereEcho? _echo;
    private readonly LumiereCrossBurst? _burst;
    private readonly Trigger[] _triggers = [];
    private readonly List<LumiereBurstEvent> _events = new(LumiereCrossBurst.MaxBursts);

    private readonly record struct Trigger(int LineIndex, int GlyphIndex, double Time, double Size, double Dy, Vector3 Color);

    private readonly LumiereLightFieldFrame _field = new();
    private readonly List<LumiereResolvedBeam> _previous = new(LumiereLight.MaxBeams);
    private LumiereTransform _transform;
    private double _time;
    private double _exit = 1;
    private double _local;
    private LumiereAudioFrame _audio;

    /// <param name="resolution">Render resolution (pixels per logical pixel) for rasterized glyphs.</param>
    public LumiereUnit(LumiereUnitOptions options, LumiereGpu gpu, float width, float height, float resolution)
    {
        _options = options;
        _width = width;
        _height = height;
        _aspect = width / (double)height;
        var tuning = options.Tuning;
        _palette = LumierePalette.Resolve(options.Theme, tuning.ThemeColorMix);
        _shots = [.. options.Shots];
        if (_shots.Length == 0) throw new ArgumentException("A Lumiere unit needs at least one shot.", nameof(options));
        // Lead profile (text region, size, camera, motes, starfall): the first shot with lyrics.
        _lead = _shots[Math.Max(0, Array.FindIndex(_shots, shot => shot.Lines.Count > 0))].Profile;
        _rigs = [.. _shots.Select((shot, index) =>
            shot.Profile.Light(new LumiereRigContext(_aspect, new LumiereRng($"{options.Seed}:{index}:{shot.Profile.Kind}:light"))))];
        // Camera moves expose at most ~1.4% of the edge; overscan 2% covers it.
        _overscan = MathF.Ceiling(MathF.Max(width, height) * 0.02f);
        _motes = new LumiereMotes(width, height, $"{options.Seed}:motes",
            _lead.Motes with { Count = (int)Math.Round(_lead.Motes.Count * tuning.MoteAmount) }, gpu.Sprites.Dot);
        if (_lead.Front is { } front && tuning.FrontBokeh && !tuning.TextOnly)
            _front = new LumiereMotes(width, height, $"{options.Seed}:front", front, gpu.Sprites.Bokeh);

        // The opening is at most 40% of the unit; units under 2 s skip it.
        var duration = options.EndTime - options.StartTime;
        var starfall = _lead.Starfall;
        var canOpen = options.Opening && duration >= 2 && !tuning.TextOnly;
        if (starfall is not null && canOpen)
            starfall = starfall with { Opening = Math.Min(starfall.Opening, Math.Max(0.8, duration * 0.4)) };
        _opening = canOpen && starfall is not null;
        _ignition = _opening ? starfall!.Ignition : 0;
        if (starfall is not null)
            _starfall = new LumiereStarfall(width, height, options.Seed, starfall, gpu.Sprites);
        // Line art per shot (seeded by unit, shot and profile).
        _lineArts = [.. _shots.Select((shot, index) => new LumiereLineArtLayer(height,
            shot.Profile.LineArt(new LumiereRigContext(_aspect, new LumiereRng($"{options.Seed}:{index}:{shot.Profile.Kind}:art"))),
            gpu.Sprites.Star))];
        // Units without an opening start from a settled starfield.
        _starOffset = starfall is not null && !_opening ? starfall.Opening + 20 : 0;
        // Keywords (theme wordColors): matched once for the unit, applied per line when built.
        var keywords = LumiereKeywords.Prepare(options.Theme.WordColors, tuning.KeywordColors);
        // Background lyric echo: sung words collected into huge hollow fragments drifting down the main beam.
        if (tuning.Echo > 0 && !tuning.TextOnly && tuning.ShowText)
            _echo = new LumiereEcho(gpu.Gl, options.Lines, width, height, options.Theme.FontFamily, options.Theme.FontWeight,
                resolution, options.Seed, _lead.EchoSize, tuning.Echo, keywords);

        _camera = new LumiereCamera(width, height, _lead.Camera, options.StartTime, options.EndTime, options.Sections,
            options.Theme.AnimationIntensity);
        // Text group: line i uses its shot's typography once current (lines outside any shot follow the previous shot).
        _lyrics = new LumiereLyricWindow(gpu.Gl, new LumiereLyricWindowOptions
        {
            Width = width,
            Height = height,
            Lines = options.Lines,
            Font = options.Theme.FontFamily,
            Weight = options.Theme.FontWeight,
            Resolution = resolution,
            Region = new LumiereRegion(_lead.Region.Cx * _aspect, _lead.Region.Cy, _lead.Region.W * _aspect, _lead.Region.H),
            HeroPx = _lead.HeroSize * height,
            Neighbors = tuning.WindowNeighbors,
            Typography = options.Typography ?? _lead.Typography,
            TypographyOf = options.Typography is null ? TypographyOfLine : null,
            Decay = _lead.Decay with { Strength = _lead.Decay.Strength * tuning.Decay },
            AlwaysFly = tuning.Trails,
            Seed = options.Seed,
            Keywords = keywords,
        });

        // Cross bursts sit under the glyphs and fire only on lines covered by a shot that declares them.
        var triggers = new List<Trigger>();
        for (var shotIndex = 0; shotIndex < _shots.Length; shotIndex++)
        {
            if (_shots[shotIndex].Profile.Burst is not { } spec) continue;
            var covered = _shots[shotIndex].Lines.ToHashSet();
            var color = LumiereColor.Mix(_palette.Light, spec.Tint, 0.85f);
            var lines = Enumerable.Range(0, options.Lines.Count)
                .Select(line => covered.Contains(line) ? _lyrics.GlyphTimes(line) : (IReadOnlyList<LumiereBurstGlyph>)[])
                .ToArray();
            triggers.AddRange(LumiereCrossBurst.Plan(spec, lines, $"{options.Seed}:{shotIndex}").Select(trigger =>
                new Trigger(trigger.LineIndex, trigger.GlyphIndex, trigger.Time, trigger.Size * spec.Size, trigger.Dy,
                    // A cross landing on a keyword takes the keyword's color.
                    _lyrics.GlyphKeyword(trigger.LineIndex, trigger.GlyphIndex) is { } keyword
                        ? LumiereKeywords.BurstColor(color, _palette.Light, keyword)
                        : color)));
        }
        _triggers = [.. triggers.OrderBy(trigger => trigger.Time)];
        if (_triggers.Length > 0) _burst = new LumiereCrossBurst(gpu.Sprites);
    }

    public LumiereTransform Camera => _transform;

    /// <summary>Folia lumiereTypographyOfLine: a line's own shot's typography, else the last shot with lyrics before it.</summary>
    private LumiereTypography TypographyOfLine(int lineIndex)
    {
        var found = _shots[0];
        foreach (var shot in _shots)
        {
            if (shot.Lines.Contains(lineIndex)) return shot.Profile.Typography;
            if (shot.Lines.Count > 0 && shot.Lines[0] < lineIndex) found = shot;
        }
        return found.Profile.Typography;
    }

    public (double X, double Y, double Scale, double Alpha) LineAnchor(int lineIndex, double time) => _lyrics.LineAnchor(lineIndex, time);
    public IReadOnlyList<LumiereResolvedBeam> Beams => _field.Beams;

    private static double Smooth(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double EaseInOutCubic(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    private int ActiveShot(double time)
    {
        var index = 0;
        for (var i = 0; i < _shots.Length; i++)
            if (_shots[i].StartTime <= time) index = i;
        return index;
    }

    public void Update(double time, LumiereAudioFrame audio)
    {
        var tuning = _options.Tuning;
        _time = time;
        _audio = audio;
        _local = time - _options.StartTime;
        // The opening overlaps: the shaft starts rising 0.4 s before ignition and is full 1.6 s later.
        var enter = _opening ? Smooth((_local - _ignition + 0.4) / 1.6) : 1;
        var ignite = _opening && _local >= _ignition ? 1.8 * Math.Exp(-(_local - _ignition) / 0.35) : 0;
        _exit = _options.FadeOut is { } fade
            ? 1 - Smooth((time - fade.Start) / Math.Max(fade.End - fade.Start, 0.05))
            : 1;
        var intensity = enter * _exit;
        _transform = _camera.At(time);

        // Bursts in flight (at most MaxBursts) and the brief lift they give the whole field.
        _events.Clear();
        var boost = 0.0;
        foreach (var trigger in _triggers)
        {
            var age = time - trigger.Time;
            if (age < 0 || age > LumiereCrossBurst.Duration) continue;
            boost += LumiereCrossBurst.LightBoost(age);
            if (_events.Count >= LumiereCrossBurst.MaxBursts) continue;
            var anchor = _lyrics.GlyphAnchor(trigger.LineIndex, trigger.GlyphIndex, time);
            _events.Add(new LumiereBurstEvent(age, anchor.X, (float)(anchor.Y + anchor.FontPx * trigger.Dy),
                (float)(anchor.FontPx * trigger.Size), trigger.Color));
        }
        // A run of crosses stacks its lift; cap it.
        boost = Math.Min(boost, 0.45);

        var response = tuning.AudioResponse;
        var index = ActiveShot(time);
        var sinceShot = time - _shots[index].StartTime;
        var handoff = index > 0 ? Smooth(sinceShot / Handoff) : 1;
        var glareK = index > 0 ? EaseInOutCubic(sinceShot / GlareMove) : 1;
        LumiereLightDrive Drive(int shotIndex, double weight) => new(
            intensity * tuning.LightIntensity * (1 + boost) * weight,
            Math.Min(1, audio.Bass) * response,
            _palette.Light,
            time - _shots[shotIndex].StartTime);

        var beams = _field.Beams;
        beams.Clear();
        LumiereLight.ResolveBeams(_rigs[index], time, _aspect, Drive(index, handoff), beams);
        var rig = _rigs[index];
        if (handoff < 1)
        {
            _previous.Clear();
            LumiereLight.ResolveBeams(_rigs[index - 1], time, _aspect, Drive(index - 1, 1 - handoff), _previous);
            beams.AddRange(_previous);
            // Stable descending sort by intensity (Array.prototype.sort is stable), keep the brightest.
            for (var i = 1; i < beams.Count; i++)
            {
                var item = beams[i];
                var j = i - 1;
                while (j >= 0 && beams[j].Intensity < item.Intensity)
                {
                    beams[j + 1] = beams[j];
                    j--;
                }
                beams[j + 1] = item;
            }
            if (beams.Count > LumiereLight.MaxBeams) beams.RemoveRange(LumiereLight.MaxBeams, beams.Count - LumiereLight.MaxBeams);
        }
        if (handoff < 1 || glareK < 1) rig = BlendRig(_rigs[index - 1], _rigs[index], handoff, glareK);

        _field.Rig = rig;
        _field.Time = time;
        // Overall loudness thickens the fog (up to +20%).
        _field.FogScale = tuning.FogDensity * (1 + boost * 0.5) * (1 + 0.2 * Math.Min(1, audio.Power) * response);
        _field.Color = _palette.Light;
        _field.GlareScale = intensity * tuning.LightIntensity * (1 + boost * 1.5 + ignite);
        _field.Dark = Vector4.Zero;
        _field.Octaves = tuning.FogOctaves;
        _field.TextRegion = _lead.Region;
    }

    /// <summary>Renders the unit onto <paramref name="output"/> (premultiplied over what is there).</summary>
    /// <param name="pixelScale">Target pixels per logical pixel.</param>
    public void Render(LumiereGpu gpu, EffectPrimitiveRenderer primitives, LumiereBinding output, float pixelScale)
    {
        var tuning = _options.Tuning;
        var camera = _transform.ToMatrix();
        LumiereGroups.Render(gpu, primitives, output, pixelScale, camera, _width, _height, _overscan, tuning,
            tuning.TextOnly ? null : (root, viewport) =>
            {
                gpu.LightField.Render(_field, root, viewport, _width, _height, _overscan);
                _echo?.Draw(primitives, root, _time, _field.Beams, _palette.Light, Smooth(_local / 1.2) * _exit);
                _starfall?.Draw(primitives, root, _local + _starOffset, _time, _field.Beams, _palette.Light, _exit);
                if (tuning.LineArt) DrawLineArt(primitives, root);
                // High frequencies make the motes sparkle (up to +50%).
                _motes.Draw(primitives, root, _time, _field.Beams, _palette.Light,
                    _exit * (1 + 0.5 * Math.Min(1, _audio.Treble) * tuning.AudioResponse));
            },
            !tuning.ShowText ? null : (root, _) =>
            {
                _burst?.Draw(primitives, root, _events);
                _lyrics.Draw(primitives, gpu.Sprites, root, new LumiereWindowFrame(_time, _field.Beams, _palette.Lit,
                    _palette.Unlit, tuning.UnlitOpacity, Smooth(_local / 0.6) * _exit, tuning.HideTrails));
            });
        if (_front is not null)
        {
            _front.Draw(primitives, camera * Matrix3x2.CreateScale(pixelScale), _time, _field.Beams, _palette.Light, _exit);
            primitives.Flush();
        }
    }

    public void Dispose()
    {
        _echo?.Dispose();
        _lyrics.Dispose();
    }

    /// <summary>
    /// Line art: each shot starts drawing 0.6 s early (the first one overlapping the opening's starfall and ignition) and
    /// fades out 0.8 s after the shot ends, so one shot's art fades while the next one draws.
    /// </summary>
    private void DrawLineArt(EffectPrimitiveRenderer primitives, Matrix3x2 root)
    {
        for (var index = 0; index < _shots.Length; index++)
        {
            var shot = _shots[index];
            var begin = index == 0 ? _options.StartTime + _ignition - 0.6 : shot.StartTime - 0.6;
            var fadeOut = index == _shots.Length - 1 ? 1 : 1 - Smooth((_time - shot.EndTime) / 0.8);
            var basis = Smooth((_time - begin) / 1.2) * fadeOut * _exit;
            var draw = (_time - begin) / 3.6;
            var fade = basis * shot.Profile.ArtGain;
            if (fade <= 0.003) continue;
            _lineArts[index].Draw(primitives, root, _time, draw, fade, _field.Beams, _palette.Light);
        }
    }

    /// <summary>Fog and glare between two rigs (beams cross-fade separately, both in the field).</summary>
    private static LumiereLightRig BlendRig(LumiereLightRig from, LumiereLightRig to, double k, double glareK)
    {
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        var fog = new LumiereFogSpec(
            Lerp(from.Fog.Density, to.Fog.Density, k),
            Lerp(from.Fog.TyndallBase, to.Fog.TyndallBase, k),
            Lerp(from.Fog.Scale, to.Fog.Scale, k),
            Lerp(from.Fog.DriftX, to.Fog.DriftX, k),
            Lerp(from.Fog.DriftY, to.Fog.DriftY, k),
            Lerp(from.Fog.Warp, to.Fog.Warp, k),
            Lerp(from.Fog.Ambient, to.Fog.Ambient, k));
        // The source moves on its own eased curve (glareK): slow to start, slow to land.
        LumiereGlareSpec? glare = from.Glare is { } a && to.Glare is { } b
            ? new LumiereGlareSpec(Lerp(a.X, b.X, glareK), Lerp(a.Y, b.Y, glareK), Lerp(a.Radius, b.Radius, glareK),
                Lerp(a.Intensity, b.Intensity, glareK), Lerp(a.Streak, b.Streak, glareK))
            : k < 0.5
                ? from.Glare is { } f ? f with { Intensity = f.Intensity * (1 - k * 2) } : null
                : to.Glare is { } g ? g with { Intensity = g.Intensity * (k * 2 - 1) } : null;
        // Caustics and interference: first half the old rig's (fading), second half the new one's (rising).
        var causticSource = k < 0.5 ? from.Caustic : to.Caustic;
        var causticK = k < 0.5 ? 1 - k * 2 : k * 2 - 1;
        var caustic = causticSource is null ? null : causticSource with
        {
            InBeam = causticSource.InBeam * causticK,
            Floor = causticSource.Floor is { } floor ? floor with { Strength = floor.Strength * causticK } : null,
        };
        var wave = k < 0.5
            ? from.Wave is { } w0 ? w0 with { Strength = w0.Strength * (1 - k * 2) } : null
            : to.Wave is { } w1 ? w1 with { Strength = w1.Strength * (k * 2 - 1) } : null;
        return new LumiereLightRig([], fog, glare, caustic, wave);
    }
}
