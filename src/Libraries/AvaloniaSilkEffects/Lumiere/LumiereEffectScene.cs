using System.Numerics;
using Avalonia;
using AvaloniaSilkEffects.Lumiere.Light;

namespace AvaloniaSilkEffects.Lumiere;

/// <summary>All the inputs of one song; a different <see cref="Seed"/> is a real track change.</summary>
public sealed record LumiereSongContext(string Seed, LumiereProgram Program, LumiereTheme Theme, LumiereSongMetadata Metadata);

public sealed class LumiereSceneOptions
{
    /// <summary>
    /// Background lyric fragments are disabled for readability. Trails stay on, with both blooms at 0.6: at full strength the graphics bloom saturates the top of a
    /// beam and the text bloom merges a column of lit glyphs sitting in it into one white blob.
    /// </summary>
    public LumiereSceneTuning Tuning { get; init; } = new() { Echo = 0, Trails = true, Bloom = 0.6f, TextBloom = 0.6f };
    /// <summary>Hard cuts between paragraphs (no transition frames).</summary>
    public bool StaticMode { get; init; }
}

/// <summary>
/// 绘光 (Lumiere) for <see cref="SilkEffectControl"/> — Folia's createLumierePixiRuntime.ts. Time is the frame's elapsed clock
/// (the host seeks it to the playback position). Per frame it resolves which paragraph scenes draw and with which transition
/// frame, keeps the current paragraph ±1 cached, and does at most one expensive thing (build a scene, build the credits,
/// release a retired scene). The dark field sits under everything and never transitions; the frame overlay sits on top.
/// A track change builds the new song's current scene while the old one still draws, then switches.
/// </summary>
public sealed class LumiereEffectScene(LumiereSongContext song, LumiereSceneOptions? options = null) : EffectScene
{
    /// <summary>How long before the last line ends the credits card is built.</summary>
    private const double CreditsPrebuild = 4;
    private const double AudioAttack = 0.06;
    private const double AudioRelease = 0.25;

    private readonly Dictionary<int, LumiereUnit> _cache = [];
    private readonly List<LumiereUnit> _retired = [];
    private readonly List<LumiereLayerFrame> _layers = [];
    private readonly LumiereOverlay _overlay = new();
    private LumiereGpu? _gpu;
    private LumiereCompositor? _compositor;
    private LumiereCredits? _credits;
    private LumiereCreditsFrame _creditsFrame = LumiereCreditsFrame.Inactive;
    private LumiereSongContext? _pending;
    private (LumiereSongContext Song, int Index, LumiereUnit Unit)? _staged;
    private PixelSize _pixelSize;
    private double _scaling = 1;
    private Vector2 _logical;
    private int _activeIndex = -1;
    private LumiereAudioFrame _smoothedAudio;
    private bool _audioPrimed;

    public LumiereSongContext Song { get; private set; } = song;
    public LumiereSceneOptions Options { get; } = options ?? new LumiereSceneOptions();
    public LumiereProgram Program => Song.Program;

    /// <summary>Raw audio features 0..1; smoothed with a fast attack and slow release, like Folia's sampler.</summary>
    public LumiereAudioFrame Audio { get; set; }
    /// <summary>While paused the scene treats audio as silence.</summary>
    public bool Paused { get; set; }

    public int ActiveParagraphIndex => _activeIndex;
    public int CachedParagraphCount => _cache.Count;
    public bool CreditsBuilt => _credits is not null;

    /// <summary>The profile kind of the shot at <paramref name="time"/> (debugging).</summary>
    public string? ShotKindAt(double time)
    {
        if (Program.Paragraphs.Count == 0) return null;
        var paragraph = Program.Paragraphs[Program.ParagraphIndexAt(time)];
        var found = paragraph.Shots.FirstOrDefault();
        foreach (var shot in paragraph.Shots) if (shot.StartTime <= time) found = shot;
        return found?.Kind;
    }

    /// <summary>Hands over to another song (or the same song with a new program / theme / metadata).</summary>
    public void SetSong(LumiereSongContext next) => _pending = next;

    public override void Initialize(EffectDevice device)
    {
        base.Initialize(device);
        _gpu = new LumiereGpu(device.Gl, device.Textures);
        _compositor = new LumiereCompositor(device.Gl);
    }

    public override void Resize(PixelSize pixelSize, double renderScaling)
    {
        if (_pixelSize == pixelSize && Math.Abs(_scaling - renderScaling) < 0.001) return;
        _pixelSize = pixelSize;
        _scaling = renderScaling;
        _logical = new Vector2(
            MathF.Max(1, (float)(pixelSize.Width / renderScaling)),
            MathF.Max(1, (float)(pixelSize.Height / renderScaling)));
        // Sizes are baked into scenes: rebuild everything for the new frame.
        ClearScenes();
        ReleaseStaged();
        InvalidateCredits();
    }

    private float PixelScale => _logical.X > 0 ? (float)(_pixelSize.Width / (double)_logical.X) : 1;
    private float Resolution => MathF.Min(2, MathF.Max(1, PixelScale));

    private LumiereUnit BuildUnit(LumiereSongContext context, int index) => new(
        LumiereUnitOptions.FromParagraph(context.Program, context.Program.Paragraphs[index], context.Theme, Options.Tuning),
        _gpu!, _logical.X, _logical.Y, Resolution);

    private void ClearScenes()
    {
        foreach (var unit in _cache.Values) unit.Dispose();
        _cache.Clear();
        foreach (var unit in _retired) unit.Dispose();
        _retired.Clear();
        _activeIndex = -1;
    }

    private void ReleaseStaged()
    {
        _staged?.Unit.Dispose();
        _staged = null;
    }

    private void InvalidateCredits()
    {
        _credits?.Dispose();
        _credits = null;
    }

    private static double SmoothAudio(double current, double target, double dt)
    {
        var tau = target > current ? AudioAttack : AudioRelease;
        return current + (target - current) * (1 - Math.Exp(-Math.Max(0, dt) / tau));
    }

    private void SampleAudio(double dt)
    {
        if (Paused)
        {
            _smoothedAudio = default;
            return;
        }
        // The first sample takes the value directly.
        if (!_audioPrimed) dt = 1;
        _audioPrimed = true;
        dt = Math.Min(dt, 1);
        var target = Audio;
        _smoothedAudio = new LumiereAudioFrame(
            SmoothAudio(_smoothedAudio.Bass, Math.Clamp(target.Bass, 0, 1), dt),
            SmoothAudio(_smoothedAudio.Treble, Math.Clamp(target.Treble, 0, 1), dt),
            SmoothAudio(_smoothedAudio.Power, Math.Clamp(target.Power, 0, 1), dt));
    }

    /// <summary>
    /// Song hand-over. Same seed (theme, metadata or lyrics changed), or nothing drawn yet: replace at once. A real track change
    /// builds the new song's current scene on one frame (the old one still draws) and switches on the next; the old scenes are
    /// then released one per frame.
    /// </summary>
    /// <returns>True when this frame already did its expensive work.</returns>
    private bool AdvanceSongSwap(double time)
    {
        if (_pending is not { } next) return false;
        if (_staged is { } staged)
        {
            Song = staged.Song;
            _pending = null;
            _retired.AddRange(_cache.Values);
            _cache.Clear();
            _cache[staged.Index] = staged.Unit;
            _activeIndex = staged.Index;
            _staged = null;
            InvalidateCredits();
            return false;
        }
        if (next.Seed == Song.Seed || _cache.Count == 0 || Paused || next.Program.Paragraphs.Count == 0)
        {
            Song = next;
            _pending = null;
            ClearScenes();
            InvalidateCredits();
            return false;
        }
        var index = next.Program.ParagraphIndexAt(time);
        _staged = (next, index, BuildUnit(next, index));
        return true;
    }

    public override void Update(in EffectFrame frame)
    {
        // Lumiere has no global post-processing; its blooms live on its own groups. MSAA stays off like Pixi's antialias: false.
        Device.PostProcess.Reset();
        if (_gpu is null || _logical.X <= 1 && _logical.Y <= 1) return;
        var time = frame.Elapsed.TotalSeconds;
        SampleAudio(frame.Delta.TotalSeconds);
        var swapped = AdvanceSongSwap(time);
        var program = Program;
        _layers.Clear();
        if (program.Paragraphs.Count == 0) return;

        var (activeIndex, layers) = LumiereTransitions.Resolve(program, time, !Options.StaticMode);
        _layers.AddRange(layers);
        if (activeIndex != _activeIndex)
        {
            _activeIndex = activeIndex;
            foreach (var index in _cache.Keys.ToArray())
            {
                if (Math.Abs(index - activeIndex) <= 1 || _layers.Any(layer => layer.Index == index)) continue;
                _cache[index].Dispose();
                _cache.Remove(index);
            }
        }
        var built = swapped;
        foreach (var layer in _layers)
        {
            if (_cache.ContainsKey(layer.Index)) continue;
            _cache[layer.Index] = BuildUnit(Song, layer.Index);
            built = true;
        }

        var lyricEnd = program.LyricEndTime;
        var hasCredits = lyricEnd is not null && Song.Metadata.HasCredits;
        _creditsFrame = hasCredits ? LumiereCreditsFrame.At(time, lyricEnd!.Value) : LumiereCreditsFrame.Inactive;
        if (!built && _staged is null)
        {
            // One expensive thing per frame: release a retired scene → prebuild the next → credits → prebuild the previous.
            var next = activeIndex + 1;
            var previous = activeIndex - 1;
            if (_retired.Count > 0)
            {
                _retired[0].Dispose();
                _retired.RemoveAt(0);
            }
            else if (next < program.Paragraphs.Count && !_cache.ContainsKey(next))
                _cache[next] = BuildUnit(Song, next);
            else if (hasCredits && _credits is null && time >= lyricEnd!.Value - CreditsPrebuild)
                BuildCredits();
            else if (previous >= 0 && !_cache.ContainsKey(previous))
                _cache[previous] = BuildUnit(Song, previous);
        }
        if (_creditsFrame.Active && _credits is null) BuildCredits();

        foreach (var layer in _layers)
            if (layer.Alpha * _creditsFrame.LyricAlpha > 0.002) _cache[layer.Index].Update(time, _smoothedAudio);
        if (_credits is not null && _creditsFrame.Active) _credits.Update(time - lyricEnd!.Value);
    }

    private void BuildCredits() =>
        _credits = new LumiereCredits(_gpu!, _logical.X, _logical.Y, Resolution, Song.Theme, Song.Metadata, Options.Tuning);

    public override void Render(EffectRenderContext context)
    {
        if (_gpu is null || _compositor is null) return;
        var primitives = context.Primitives;
        primitives.Flush();
        var gl = _gpu.Gl;
        var output = LumiereBinding.Capture(gl);
        var pixelScale = PixelScale;
        var size = new Vector2(context.PixelSize.Width, context.PixelSize.Height);
        var dark = LumiereDarkField.Resolve(Song.Theme.Background, Options.Tuning.DarkField);
        if (dark.A > 0.002f) primitives.DrawRectangle(Matrix3x2.Identity, size, dark);
        primitives.Flush();

        var layerTarget = _compositor.Layer;
        LumiereBinding LayerBinding()
        {
            layerTarget.EnsureSize(context.PixelSize.Width, context.PixelSize.Height);
            layerTarget.Bind(clear: true);
            return new LumiereBinding(layerTarget.Framebuffer, 0, 0, layerTarget.Width, layerTarget.Height);
        }

        foreach (var layer in _layers)
        {
            if (!_cache.TryGetValue(layer.Index, out var unit)) continue;
            var alpha = (float)(layer.Alpha * _creditsFrame.LyricAlpha);
            if (alpha <= 0.002f) continue;
            var blur = (float)Math.Max(layer.Blur, _creditsFrame.LyricBlur);
            if (alpha >= 0.999f && Math.Abs(layer.Scale - 1) < 1e-4 && blur <= 0.3f)
            {
                unit.Render(_gpu, primitives, output, pixelScale);
                continue;
            }
            unit.Render(_gpu, primitives, LayerBinding(), pixelScale);
            primitives.Flush();
            _compositor.Composite(output, alpha, (float)layer.Scale, blur * pixelScale);
        }

        if (_credits is not null && _creditsFrame.Active && _creditsFrame.PosterAlpha > 0.002)
        {
            _credits.Render(_gpu, primitives, LayerBinding(), pixelScale);
            primitives.Flush();
            _compositor.Composite(output, (float)_creditsFrame.PosterAlpha, (float)_creditsFrame.PosterScale, 0);
        }

        output.Restore(gl);
        var tuning = Options.Tuning;
        if (tuning.OverlayFrame && !tuning.TextOnly)
            _overlay.Draw(primitives, _logical.X, _logical.Y, pixelScale,
                LumierePalette.Resolve(Song.Theme, tuning.ThemeColorMix).Light);
        output.Restore(gl);
    }

    public override void DisposeGpuResources()
    {
        ClearScenes();
        ReleaseStaged();
        InvalidateCredits();
        _compositor?.Dispose();
        _compositor = null;
        _gpu?.Dispose();
        _gpu = null;
    }
}
