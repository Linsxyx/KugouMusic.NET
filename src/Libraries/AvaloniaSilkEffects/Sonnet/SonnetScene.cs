using Avalonia;
using System.Diagnostics;
using System.Numerics;

namespace AvaloniaSilkEffects.Sonnet;

public readonly record struct SonnetAudioFrame(float Power, float Bass, float Vocal);

/// <summary>A seek-stable Sonnet v0.7.2 scene driven entirely by EffectFrame.Elapsed.</summary>
public sealed class SonnetScene : EffectScene
{
    private readonly EffectContainer _stage = new();
    private readonly Dictionary<int, ParagraphView> _cache = [];
    private PixelSize _size;
    private PixelSize _physicalSize;
    private double _scaling = 1;
    private int _activeParagraph = -1;
    private int _buildCursor;
    private EffectContainer? _overlay;
    private ShapeNode? _swapCover;
    private PendingSongSwap? _songSwap;

    public SonnetScene(SonnetProgram program, SonnetTheme theme, SonnetTuning? tuning = null)
    {
        Program = program;
        Theme = theme;
        Options = new() { Tuning = tuning ?? new() };
        CurrentSong = new(program.Seed, program.Seed, program, theme);
    }

    public SonnetScene(SonnetSongContext song, SonnetSceneOptions? options = null)
    {
        CurrentSong = song;
        Program = song.Program;
        Theme = song.Theme;
        Metadata = song.Metadata ?? new();
        Options = options ?? new();
    }

    public SonnetProgram Program { get; private set; }
    public SonnetTheme Theme { get; set; }
    public SonnetSceneOptions Options { get; }
    public SonnetTuning Tuning => Options.Tuning;
    public SonnetModulation Modulation { get; } = new();
    public SonnetSongContext CurrentSong { get; private set; }
    public SonnetSongMetadata Metadata { get; set; } = new();
    public SonnetAudioFrame Audio { get; set; }
    public int ActiveParagraphIndex => _activeParagraph;
    public SonnetShotKind? ActiveShotKind { get; private set; }
    public int CachedParagraphCount => _cache.Count;

    public void SetProgram(SonnetProgram program)
    {
        Program = program;
        CurrentSong = CurrentSong with { Seed = program.Seed, Program = program };
        ClearViews();
    }

    public void SetSong(SonnetSongContext song, SonnetSongSwapMode mode = SonnetSongSwapMode.Animated)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (mode == SonnetSongSwapMode.Immediate || song.TrackIdentity == CurrentSong.TrackIdentity)
        {
            _songSwap = null;
            CommitSong(song);
            return;
        }
        _songSwap = new(song, Stopwatch.GetTimestamp(), false);
    }

    public override void Resize(PixelSize pixelSize, double renderScaling)
    {
        if (_physicalSize == pixelSize && Math.Abs(_scaling - renderScaling) < 0.001) return;
        _physicalSize = pixelSize;
        _scaling = renderScaling;
        _size = new(
            Math.Max(1, (int)Math.Round(pixelSize.Width / renderScaling)),
            Math.Max(1, (int)Math.Round(pixelSize.Height / renderScaling)));
        _stage.Scale = new((float)renderScaling);
        ClearViews();
        if (_overlay is not null) _stage.Remove(_overlay);
        _overlay = SonnetMgBuilder.BuildOverlay(Theme, _size.Width, _size.Height);
        _stage.Add(_overlay);
        RebuildSwapCover();
    }

    public override void Update(in EffectFrame frame)
    {
        UpdateSongSwap();
        ConfigurePostProcess((float)frame.Elapsed.TotalSeconds);
        if (Program.Paragraphs.Count == 0 || _size.Width <= 0 || _size.Height <= 0) return;
        var time = frame.Elapsed.TotalSeconds;
        var paragraphIndex = SonnetProgramCompiler.FindParagraphIndexAtTime(Program, time);
        if (!_cache.ContainsKey(paragraphIndex)) BuildParagraph(paragraphIndex);
        _activeParagraph = paragraphIndex;

        foreach (var (index, view) in _cache)
        {
            view.Root.IsVisible = index == paragraphIndex;
            if (index == paragraphIndex) UpdateParagraph(view, time);
        }
        Prune(paragraphIndex);

        // Match Folia's one-expensive-build-per-frame pre-roll policy.
        var next = paragraphIndex + 1;
        var previous = paragraphIndex - 1;
        if (next < Program.Paragraphs.Count && !_cache.ContainsKey(next)) BuildParagraph(next);
        else if (previous >= 0 && !_cache.ContainsKey(previous)) BuildParagraph(previous);
        _buildCursor++;
    }

    public override void Render(EffectRenderContext context) => context.Render(_stage);

    public override void DisposeGpuResources() => ClearViews();

    private void BuildParagraph(int index)
    {
        var paragraph = Program.Paragraphs[index];
        var root = new EffectContainer { IsVisible = false };
        var shots = new List<ShotView>();
        for (var shotIndex = 0; shotIndex < paragraph.Shots.Count; shotIndex++)
        {
            var shot = paragraph.Shots[shotIndex];
            var lines = paragraph.Lines.Where(item => shot.LineIndices.Contains(item.SourceIndex)).ToArray();
            var segmentsByLine = lines.Select(item => (IReadOnlyList<SonnetSemanticSegment>)item.Segments).ToArray();
            var segments = segmentsByLine.SelectMany(item => item).ToArray();
            var wordCount = Math.Max(1, segments.Count(item => item.IsWordLike));
            var heroScale = shot.Kind == SonnetShotKind.TypeImpact ? 1.55f : shot.Kind == SonnetShotKind.QuietTableau ? 0.82f : 1;
            var baseFontSize = Math.Clamp(_size.Width / Math.Max(7f, wordCount * 2.15f) * heroScale, 24, 112);
            var placements = SonnetTypographyLayout.Resolve(
                segmentsByLine, shot.Kind, paragraph.Kind, _size.Width, _size.Height, baseFontSize,
                (text, size, weight) =>
                {
                    var measured = Device.Textures.MeasureText(text, Theme.FontFamily, size, weight);
                    return (measured.X, measured.Y);
                }, Theme.FontWeight);

            var shotRoot = new EffectContainer { IsVisible = false };
            var mg = SonnetMgBuilder.BuildShot(shot, Theme, _size.Width, _size.Height,
                SonnetRandom.Hash($"{Program.Seed}:{shot.Id}:mg"), Tuning);
            if (!Tuning.ShowOnlyText) shotRoot.Add(mg.Root);
            var glyphs = new List<GlyphView>();
            var guides = new List<SonnetGuideView>();
            for (var placementIndex = 0; placementIndex < placements.Count; placementIndex++)
            {
                var placement = placements[placementIndex];
                var segment = segments[placement.SegmentIndex];
                var fontSize = baseFontSize * placement.FontScale;
                var weight = SonnetTypographyLayout.ResolveFontWeight(Theme.FontWeight, placement.Role);
                var decorSeed = SonnetRandom.Hash($"{shot.Id}:{placementIndex}:{segment.Text}");
                if (!Tuning.ShowOnlyText && Tuning.ShowGiantDecorativeText && placement.Role == SonnetSegmentRole.Hero)
                {
                    var giant = Text(segment.Text, Math.Min(_size.Width, _size.Height) * 0.48f, 300,
                        Theme.Secondary with { A = 0.045f });
                    giant.Anchor = new(0.5f);
                    giant.Position = new(-_size.Width * 0.14f, _size.Height * (placementIndex % 2 == 0 ? -0.18f : 0.2f));
                    giant.Rotation = placementIndex % 2 == 0 ? -0.09f : 0.08f;
                    shotRoot.Add(giant);
                }
                if (!Tuning.ShowOnlyText && Tuning.ShowGuide)
                {
                    var guide = SonnetMgBuilder.BuildGuide(segment, placement, fontSize, Theme, decorSeed);
                    shotRoot.Add(guide.Root);
                    guides.Add(guide);
                }
                if (!Tuning.ShowOnlyText && Tuning.ShowFixedGeo &&
                    (placement.Role is SonnetSegmentRole.Hero or SonnetSegmentRole.SemiHero || decorSeed % 100 < 28))
                    shotRoot.Add(SonnetMgBuilder.BuildFrame(placement, fontSize, Theme, decorSeed));
                var glyphLayout = SonnetMotion.BuildGlyphs(segment, placement, fontSize,
                    text => Device.Textures.MeasureText(text, Theme.FontFamily, fontSize, weight).X,
                    shot.StartTime, shot.EndTime);
                foreach (var glyph in glyphLayout)
                {
                    var wrapper = new EffectContainer { Position = glyph.Position, Alpha = 0 };
                    var cyan = Text(glyph.Text, fontSize, weight, new(0, 1, 1, 0.65f), EffectBlendMode.Screen);
                    var red = Text(glyph.Text, fontSize, weight, new(1, 0, 0.27f, 0.65f), EffectBlendMode.Screen);
                    var ghostA = Text(glyph.Text, fontSize, weight, Theme.Primary with { A = 0.24f });
                    var ghostB = Text(glyph.Text, fontSize, weight, Theme.Primary with { A = 0.12f });
                    var core = Text(glyph.Text, fontSize, weight, Theme.Primary);
                    wrapper.Add(ghostB).Add(ghostA);
                    if (Tuning.ShowChromaticSplit)
                        wrapper.Add(cyan).Add(red);
                    wrapper.Add(core);
                    shotRoot.Add(wrapper);
                    glyphs.Add(new(wrapper, cyan, red, ghostA, ghostB, glyph, placement.Role, fontSize));
                }
            }
            root.Add(shotRoot);
            var hero = placements.FirstOrDefault(item => item.Role == SonnetSegmentRole.Hero);
            shots.Add(new(shot, shotRoot, glyphs, guides, mg, new(hero?.X ?? 0, hero?.Y ?? 0)));
        }
        _stage.Add(root);
        if (_overlay is not null)
        {
            _stage.Remove(_overlay);
            _stage.Add(_overlay);
        }
        _cache[index] = new(paragraph, root, shots);
    }

    private void UpdateParagraph(ParagraphView paragraph, double time)
    {
        var shotIndex = 0;
        for (var index = paragraph.Shots.Count - 1; index >= 0; index--)
            if (time >= paragraph.Shots[index].Shot.StartTime) { shotIndex = index; break; }
        var transitionSeed = SonnetRandom.Hash($"{Program.Seed}:{paragraph.Paragraph.Id}:transition-frame");
        var shotTransition = SonnetTransitions.ResolveShot(paragraph.Shots.Select(item => item.Shot).ToArray(), shotIndex, time,
            Tuning.EnableTransitions, transitionSeed);
        var paragraphTransition = SonnetTransitions.ResolveParagraph(paragraph.Paragraph, time, Tuning.EnableTransitions, transitionSeed);
        var transition = shotTransition != SonnetMotion.IdleTransition ? shotTransition : paragraphTransition;
        paragraph.Root.Alpha = (float)transition.Alpha;
        Device.PostProcess.Blur = (float)(transition.Blur / 14);
        Device.PostProcess.Glitch = (float)transition.Glitch;
        Device.PostProcess.Seed = (float)transition.GlitchSeed;

        for (var index = 0; index < paragraph.Shots.Count; index++)
        {
            var view = paragraph.Shots[index];
            view.Root.IsVisible = index == shotIndex;
            if (index != shotIndex) continue;
            ActiveShotKind = view.Shot.Kind;
            UpdateShot(view, time);
        }
    }

    private void UpdateShot(ShotView view, double time)
    {
        var progress = SonnetMotion.ShotProgress(view.Shot, time);
        var camera = SonnetMotion.ShotFrame(view.Shot.Kind, progress);
        var phase = SonnetRandom.Hash(view.Shot.Id) % 1024 / 1024d * Math.PI * 2;
        var revealDone = view.Glyphs.Count == 0 ? view.Shot.EndTime : view.Glyphs.Max(item => item.Placement.StartTime);
        var breathWeight = SonnetMotion.BreathWeight(time, revealDone);
        var breath = SonnetMotion.CameraBreath(time, phase);
        var cameraIntensity = Tuning.CameraIntensity * AnimationScale();
        var scale = view.Shot.Camera.Zoom * (1 + (camera.Scale - 1) * cameraIntensity) *
            (1 + breath.Scale * breathWeight * cameraIntensity);
        view.Root.Pivot = view.Focus;
        view.Root.Position = new(_size.Width * (float)(0.5 + view.Shot.Camera.X + camera.X * cameraIntensity + breath.X * breathWeight),
            _size.Height * (float)(0.48 + view.Shot.Camera.Y + camera.Y * cameraIntensity + breath.Y * breathWeight));
        view.Root.Scale = new((float)scale);
        view.Root.Rotation = (float)((view.Shot.Camera.Rotation + camera.Rotation + breath.Rotation * breathWeight) * cameraIntensity);
        var cameraOffset = new Vector2(
            _size.Width * (float)(camera.X * cameraIntensity + breath.X * breathWeight),
            _size.Height * (float)(camera.Y * cameraIntensity + breath.Y * breathWeight));
        view.Mg.Update(time, view.Shot.StartTime, Audio, cameraOffset, (float)camera.Scale);

        foreach (var guide in view.Guides)
        {
            var active = time >= guide.StartTime && time <= guide.EndTime;
            guide.Root.IsVisible = active && Tuning.ShowGuide && !Tuning.ShowOnlyText;
            if (!active) continue;
            var guideProgress = SonnetMotion.Clamp01(
                (time - guide.StartTime) / Math.Max(0.001, guide.EndTime - guide.StartTime));
            guide.Update(guideProgress);
        }

        foreach (var glyph in view.Glyphs)
        {
            var glyphProgress = SonnetMotion.SegmentProgress(glyph.Placement.StartTime, glyph.Placement.SettleTime, time);
            var waiting = time < glyph.Placement.StartTime;
            var offset = (float)((1 - glyphProgress) * Tuning.TypographyMotion * AnimationScale());
            glyph.Wrapper.Position = glyph.Placement.Position + glyph.Placement.Entrance * offset;
            glyph.Wrapper.Rotation = glyph.Placement.EntryRotation * offset;
            glyph.Wrapper.Alpha = waiting ? 0 : (float)(0.16 + glyphProgress * 0.84);
            var glyphScale = glyph.Role == SonnetSegmentRole.Hero && view.Shot.Kind == SonnetShotKind.TypeImpact
                ? 0.52f + (float)glyphProgress * 0.48f : 0.86f + (float)glyphProgress * 0.14f;
            glyph.Wrapper.Scale = new(glyphScale);
            var caOffset = glyph.FontSize * (glyph.Role is SonnetSegmentRole.Hero or SonnetSegmentRole.SemiHero ? 0.025f : 0.01f) *
                (float)(1 - SonnetMotion.EaseInOut(glyphProgress) * 0.8);
            glyph.Cyan.Position = new(-caOffset, caOffset * 0.5f);
            glyph.Red.Position = new(caOffset, -caOffset * 0.5f);
            var ghostProgress = (float)SonnetMotion.Clamp01((time - glyph.Placement.StartTime) / 0.42);
            var ghostVisible = !waiting && ghostProgress is > 0 and < 1;
            glyph.GhostA.IsVisible = ghostVisible;
            glyph.GhostB.IsVisible = ghostVisible;
            var spread = 1 - MathF.Pow(1 - ghostProgress, 3);
            glyph.GhostA.Position = new(0, glyph.FontSize * 0.18f * spread);
            glyph.GhostB.Position = new(0, glyph.FontSize * 0.31f * spread);
        }
    }

    private TextNode Text(string text, float size, int weight, EffectColor color, EffectBlendMode blend = EffectBlendMode.Alpha) => new()
    {
        Text = text, FontFamily = Theme.FontFamily, FontSize = size, FontWeight = weight,
        Color = color, RasterScale = Tuning.TextureResolution, Anchor = new(0.5f), BlendMode = blend,
    };

    private void ConfigurePostProcess(float time)
    {
        Device.PostProcess.Reset();
        Device.PostProcess.Time = time;
        Device.PostProcess.ResolutionScale = 0.75f;
        Device.PostProcess.Glow = Tuning.ShowOnlyText ? 0 : 0.18f;
        if (!Tuning.PostProcessEnabled) return;
        Device.PostProcess.Grain = Tuning.PostProcessGrain * 0.35f;
        Device.PostProcess.Contrast = Tuning.PostProcessContrast * 0.5f;
        Device.PostProcess.RgbSplit = Tuning.PostProcessRgbShift;
        Device.PostProcess.Halftone = Tuning.PostProcessHalftone;
        Device.PostProcess.Vignette = Tuning.PostProcessVignette;
        Device.PostProcess.LensDistortion = Tuning.PostProcessLensDistortion;
        Device.PostProcess.LensDispersion = Tuning.PostProcessLensDispersion;
    }

    private float AnimationScale() => Theme.AnimationIntensity switch
    {
        SonnetAnimationIntensity.Calm => 0.65f,
        SonnetAnimationIntensity.Chaotic => 1.35f,
        _ => 1,
    };

    private void UpdateSongSwap()
    {
        if (_songSwap is null)
        {
            if (_swapCover is not null) _swapCover.IsVisible = false;
            return;
        }

        const double durationSeconds = 0.56;
        var elapsed = Stopwatch.GetElapsedTime(_songSwap.StartedAt).TotalSeconds;
        var progress = Math.Clamp(elapsed / durationSeconds, 0, 1);
        if (!_songSwap.Committed && progress >= 0.5)
        {
            CommitSong(_songSwap.Song);
            _songSwap = _songSwap with { Committed = true };
        }
        if (_swapCover is not null)
        {
            var alpha = progress < 0.5
                ? SonnetMotion.EaseInOut(progress * 2)
                : 1 - SonnetMotion.EaseInOut((progress - 0.5) * 2);
            _swapCover.Color = CurrentSong.Theme.Background;
            _swapCover.Alpha = (float)alpha;
            _swapCover.IsVisible = alpha > 0.001;
        }
        if (progress >= 1)
        {
            _songSwap = null;
            if (_swapCover is not null) _swapCover.IsVisible = false;
        }
    }

    private void CommitSong(SonnetSongContext song)
    {
        CurrentSong = song;
        Program = song.Program;
        Theme = song.Theme;
        Metadata = song.Metadata ?? new();
        ClearViews();
        if (_overlay is not null) _stage.Remove(_overlay);
        _overlay = _size.Width > 0 && _size.Height > 0
            ? SonnetMgBuilder.BuildOverlay(Theme, _size.Width, _size.Height)
            : null;
        if (_overlay is not null) _stage.Add(_overlay);
        if (_swapCover is not null)
        {
            _stage.Remove(_swapCover);
            _stage.Add(_swapCover);
        }
    }

    private void RebuildSwapCover()
    {
        if (_swapCover is not null) _stage.Remove(_swapCover);
        _swapCover = new ShapeNode
        {
            Size = new(_size.Width, _size.Height),
            Color = Theme.Background,
            IsVisible = false,
        };
        _stage.Add(_swapCover);
    }

    private void Prune(int active)
    {
        foreach (var index in _cache.Keys.Where(index => Math.Abs(index - active) > 1).ToArray())
        {
            _stage.Remove(_cache[index].Root);
            _cache.Remove(index);
        }
    }

    private void ClearViews()
    {
        foreach (var view in _cache.Values) _stage.Remove(view.Root);
        _cache.Clear();
        _activeParagraph = -1;
        ActiveShotKind = null;
    }

    private sealed record GlyphView(EffectContainer Wrapper, TextNode Cyan, TextNode Red, TextNode GhostA,
        TextNode GhostB, SonnetGlyphPlacement Placement, SonnetSegmentRole Role, float FontSize);
    private sealed record ShotView(SonnetShot Shot, EffectContainer Root, List<GlyphView> Glyphs,
        List<SonnetGuideView> Guides, SonnetMgView Mg, Vector2 Focus);
    private sealed record ParagraphView(SonnetParagraph Paragraph, EffectContainer Root, List<ShotView> Shots);
    private sealed record PendingSongSwap(SonnetSongContext Song, long StartedAt, bool Committed);
}
