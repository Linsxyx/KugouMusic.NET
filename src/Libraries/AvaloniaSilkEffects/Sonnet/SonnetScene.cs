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
    private readonly List<int> _pruneBuffer = [];
    private PixelSize _size;
    private PixelSize _physicalSize;
    private double _scaling = 1;
    private int _activeParagraph = -1;
    private int _buildCursor;
    private EffectContainer? _overlay;
    private EffectContainer? _credits;
    private SonnetSongMetadata _metadata = new();
    private ShapeNode? _swapCover;
    private PendingSongSwap? _songSwap;

    public SonnetScene(SonnetProgram program, SonnetTheme theme, SonnetTuning? tuning = null)
    {
        Program = program;
        Theme = theme;
        Options = new SonnetSceneOptions { Tuning = tuning ?? new SonnetTuning() };
        CurrentSong = new SonnetSongContext(program.Seed, program.Seed, program, theme);
    }

    public SonnetScene(SonnetSongContext song, SonnetSceneOptions? options = null)
    {
        CurrentSong = song;
        Program = song.Program;
        Theme = song.Theme;
        Metadata = song.Metadata ?? new SonnetSongMetadata();
        Options = options ?? new SonnetSceneOptions();
    }

    public SonnetProgram Program { get; private set; }
    public SonnetTheme Theme { get; set; }
    public SonnetSceneOptions Options { get; }
    public SonnetTuning Tuning => Options.Tuning;
    public SonnetModulation Modulation { get; } = new();
    public SonnetSongContext CurrentSong { get; private set; }
    public SonnetSongMetadata Metadata
    {
        get => _metadata;
        set
        {
            if (_metadata == value) return;
            _metadata = value;
            RebuildChrome();
        }
    }
    public SonnetAudioFrame Audio { get; set; }
    public int ActiveParagraphIndex => _activeParagraph;
    public SonnetShotKind? ActiveShotKind { get; private set; }
    public int CachedParagraphCount => _cache.Count;
#if DEBUG
    public string? ActivePresetDebugLabel { get; private set; }
    private SonnetShot? _debugShot;
#endif

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
        _songSwap = new PendingSongSwap(song, Stopwatch.GetTimestamp(), false);
    }

    public override void Resize(PixelSize pixelSize, double renderScaling)
    {
        if (_physicalSize == pixelSize && Math.Abs(_scaling - renderScaling) < 0.001) return;
        _physicalSize = pixelSize;
        _scaling = renderScaling;
        _size = new PixelSize(
            Math.Max(1, (int)Math.Round(pixelSize.Width / renderScaling)),
            Math.Max(1, (int)Math.Round(pixelSize.Height / renderScaling)));
        _stage.Scale = new Vector2((float)renderScaling);
        ClearViews();
        RebuildChrome();
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
            if (index == paragraphIndex) UpdateParagraph(view, index, time);
        }
        UpdateCredits(time);
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
        var sceneSeed = SonnetRandom.Hash($"{Program.Seed}:{paragraph.Id}");
        root.Add(SonnetMgBuilder.BuildSceneBackdrop(
            Theme, _size.Width, _size.Height, sceneSeed, Tuning, Options.TransparentBackground));
        var shots = new List<ShotView>();
        for (var shotIndex = 0; shotIndex < paragraph.Shots.Count; shotIndex++)
        {
            var shot = paragraph.Shots[shotIndex];
            var lines = paragraph.Lines.Where(item => shot.LineIndices.Contains(item.SourceIndex)).ToArray();
            var segmentsByLine = lines
                .Select(item => (IReadOnlyList<SonnetSemanticSegment>)item.Segments
                    .Where(segment => !string.IsNullOrWhiteSpace(segment.Text)).ToArray())
                .Where(segments => segments.Count > 0).ToArray();
            var segments = segmentsByLine.SelectMany(item => item).ToArray();
            var wordCount = Math.Max(1, segments.Count(item => item.IsWordLike));
            var heroScale = shot.Kind == SonnetShotKind.TypeImpact ? 1.55f : shot.Kind == SonnetShotKind.QuietTableau ? 0.82f : 1;
            var baseFontSize = Math.Clamp(_size.Width / Math.Max(7f, wordCount * 2.15f) * heroScale, 24, 112);
            var placements = SonnetTypographyLayout.Resolve(
                segmentsByLine, shot.Kind, paragraph.Kind, _size.Width, _size.Height, baseFontSize,
                (text, size, weight) =>
                {
                    var measured = EffectTextureCache.MeasureText(text, Theme.FontFamily, size, weight);
                    return (measured.X, measured.Y);
                }, Theme.FontWeight);

            var shotRoot = new EffectContainer { IsVisible = false };
            var shotSeed = unchecked(sceneSeed + (uint)(shotIndex * 97));
            var mg = SonnetMgBuilder.BuildShot(
                shot, Theme, _size.Width, _size.Height, shotSeed, Tuning);
            if (!Tuning.ShowOnlyText) shotRoot.Add(mg.Root);
            var glyphs = new List<GlyphView>();
            var tracking = new List<IReadOnlyList<(Vector2 Position, double StartTime, bool IsBackgroundShape)>>();
            var decorationTracking = new List<IReadOnlyList<(Vector2 Position, double StartTime, bool IsBackgroundShape)>>();
            var guides = new List<SonnetGuideView>();
            var frames = new List<SonnetFrameDecorView>();
            // Folia's shot stacking: guides, then the text layer whose bottom holds frame decor and
            // text geometry, then the shared aberration copies, then every glyph wrapper.
            var guideLayer = new EffectContainer();
            var textLayer = new EffectContainer();
            var behindTextLayer = new EffectContainer();
            var caLayer = new EffectContainer();
            textLayer.Add(behindTextLayer).Add(caLayer);
            shotRoot.Add(guideLayer).Add(textLayer);
            // Virtual instrumental lines can share one shot; the staff belongs to the shot.
            var staffAdded = false;
            for (var placementIndex = 0; placementIndex < placements.Count; placementIndex++)
            {
                var placement = placements[placementIndex];
                var segment = segments[placement.SegmentIndex];
                var fontSize = baseFontSize * placement.FontScale;
                var weight = SonnetTypographyLayout.ResolveFontWeight(Theme.FontWeight, placement.Role);
                var decorSeed = SonnetRandom.Hash($"{shot.Id}:{placementIndex}:{segment.Text}");
                var isDecoration = placement.Role == SonnetSegmentRole.Decoration;
                var normalSeed = SonnetRandom.Hash(FormattableString.Invariant(
                    $"{segment.Text}:{segment.StartOffset}:{segment.EndOffset}:{placement.SegmentIndex}:normal-offset"));
                var normalOffset = SonnetMotion.SegmentNormalOffset(placement.Role,
                    placement.LayoutDirection == SonnetLayoutDirection.Vertical, placement.Rotation, fontSize,
                    normalSeed / (double)uint.MaxValue);
                placement = placement with { X = placement.X + normalOffset.X, Y = placement.Y + normalOffset.Y };
                // Folia draws this from Math.random; seed it so seeking always lands on the same frame.
                var depthRandom = new Random(unchecked((int)decorSeed));
                var depth = (float)SonnetMotion.SegmentDepth(placement.Role, depthRandom.NextDouble);
                var glowColor = SonnetTypographyLayout.IsEmphasis(placement.Role) ? Theme.Primary : Theme.Accent;
                if (segment.Text == SonnetStaffView.Marker)
                {
                    if (staffAdded) continue;
                    staffAdded = true;
                    var staff = new SonnetStaffView(placement, Theme, baseFontSize, shot.StartTime, _size.Width);
                    textLayer.Add(staff.Root);
                    if (!isDecoration && !Tuning.ShowOnlyText && Tuning.ShowGuide)
                    {
                        var staffGuide = SonnetMgBuilder.BuildGuide(segment, placement, fontSize, Theme, decorSeed);
                        guideLayer.Add(staffGuide.Root);
                        guides.Add(staffGuide);
                    }
                    var staffPosition = new Vector2(placement.X, placement.Y);
                    glyphs.Add(new GlyphView(staff.Root, null, null, null, null, [],
                        new SonnetGlyphPlacement(segment.Text, staffPosition, new Vector2(placement.EnterX, placement.EnterY),
                            0, shot.StartTime, shot.StartTime + 0.5),
                        placement.Role, placement.Rotation, fontSize, 0, 0, false, staff));
                    (isDecoration ? decorationTracking : tracking).Add([(staffPosition, shot.StartTime, false)]);
                    continue;
                }
                if (!isDecoration && !Tuning.ShowOnlyText && Tuning.ShowGuide)
                {
                    var guide = SonnetMgBuilder.BuildGuide(segment, placement, fontSize, Theme, decorSeed);
                    guideLayer.Add(guide.Root);
                    guides.Add(guide);
                }
                var glyphLayout = SonnetMotion.BuildGlyphs(segment, placement, fontSize,
                    text => EffectTextureCache.MeasureText(text, Theme.FontFamily, fontSize, weight).X,
                    shot.StartTime, shot.EndTime);
                var frameSpec = SonnetFrameDecorView.ResolveSpec(segment);
                if (!Tuning.ShowOnlyText && Tuning.ShowFixedGeo && frameSpec.Applied &&
                    !isDecoration && glyphLayout.Count > 0)
                {
                    var frameDecor = new SonnetFrameDecorView(placement, fontSize, Theme, frameSpec.Variant,
                        glyphLayout[0].StartTime, shot.StartTime, shot.EndTime);
                    behindTextLayer.Add(frameDecor.Root);
                    frames.Add(frameDecor);
                }
                var textSeed = SonnetTextFixedGeo.Seed(segment.Text, placement.SegmentIndex);
                var chorusEffect = SonnetTextFixedGeo.IsChorusEffect(textSeed, paragraph.Kind);
                if (!Tuning.ShowOnlyText && Tuning.ShowFixedGeo && SonnetTextFixedGeo.ShouldApply(textSeed, chorusEffect) &&
                    !isDecoration && segment.IsWordLike && !frameSpec.Applied && glyphLayout.Count > 0)
                {
                    var shape = SonnetTextFixedGeo.Build(textSeed, chorusEffect, fontSize, _size.Width, Theme);
                    var shapeWrapper = new EffectContainer { Alpha = 0 };
                    shapeWrapper.Add(shape);
                    behindTextLayer.Add(shapeWrapper);
                    var first = glyphLayout[0];
                    glyphs.Add(new GlyphView(shapeWrapper, null, null, null, null, [],
                        first with
                        {
                            Position = new Vector2(placement.X, placement.Y),
                            Entrance = new Vector2(placement.EnterX, placement.EnterY),
                            EntryRotation = 0,
                        },
                        placement.Role, placement.Rotation, fontSize, -0.5f - textSeed % 5 * 0.1f, 0, true));
                }
                if (glyphLayout.Count > 0)
                    (isDecoration ? decorationTracking : tracking).Add(
                        glyphLayout.Select(glyph => (glyph.Position, glyph.StartTime, false)).ToArray());
                var strokeWidth = Math.Clamp(fontSize * 0.006f, 1, 8);
                var emphasis = SonnetTypographyLayout.IsEmphasis(placement.Role);
                var ghostDuration = Math.Min(0.7, Math.Max(0.4, (shot.EndTime - shot.StartTime) * 0.12 + 0.1));
                var ghostSide = normalSeed % 2 == 0 ? 1 : -1;
                var screenNormal = placement.LayoutDirection == SonnetLayoutDirection.Vertical ? Vector2.UnitX : Vector2.UnitY;
                var ghostNormal = Vector2.Transform(screenNormal, Matrix3x2.CreateRotation(-placement.Rotation));
                foreach (var glyph in glyphLayout)
                {
                    var wrapper = new EffectContainer { Position = glyph.Position, Rotation = placement.Rotation, Alpha = 0 };
                    TextNode core;
                    if (isDecoration)
                    {
                        // Giant hollow echo: outline only, faint, and never part of the aberration.
                        core = Text(glyph.Text, fontSize, weight, glowColor);
                        core.StrokeWidth = strokeWidth;
                        core.Alpha = 0.2f;
                    }
                    else core = GlowText(glyph.Text, fontSize, weight, Theme.Primary, glowColor with { A = 0.8f });
                    var ghosts = new List<GhostView>();
                    if (placement.Role == SonnetSegmentRole.SemiHero)
                    {
                        for (var layer = 1; layer <= 2; layer++)
                        {
                            var ghost = Text(glyph.Text, fontSize, weight, glowColor);
                            ghost.StrokeWidth = strokeWidth;
                            ghost.IsVisible = false;
                            wrapper.Add(ghost);
                            ghosts.Add(new GhostView(ghost,
                                ghostNormal * ghostSide * (layer == 1 ? 1 : 1.7f) * fontSize * 0.85f,
                                layer == 1 ? 0.3f : 0.16f));
                        }
                    }
                    wrapper.Add(core);
                    EffectContainer? caWrapper = null;
                    TextNode? cyan = null;
                    TextNode? red = null;
                    if (!isDecoration && Tuning.ShowChromaticSplit)
                    {
                        var caAlpha = emphasis ? 0.8f : 0.5f;
                        cyan = Text(glyph.Text, fontSize, weight, new EffectColor(0, 1, 1, caAlpha), EffectBlendMode.Screen);
                        red = Text(glyph.Text, fontSize, weight, new EffectColor(1, 0, 0.267f, caAlpha), EffectBlendMode.Screen);
                        caWrapper = new EffectContainer { Alpha = 0 };
                        caWrapper.Add(cyan).Add(red);
                        caLayer.Add(caWrapper);
                    }
                    textLayer.Add(wrapper);
                    glyphs.Add(new GlyphView(wrapper, caWrapper, cyan, red, core, ghosts, glyph, placement.Role,
                        placement.Rotation, fontSize, depth, ghostDuration, false));
                }
            }
            if (tracking.Count == 0) tracking = decorationTracking;
            root.Add(shotRoot);
            var hero = placements.FirstOrDefault(item => item.Role == SonnetSegmentRole.Hero);
            var poster = shot.Kind == SonnetShotKind.PosterBlocks;
            var basePosition = new Vector2(
                _size.Width * (float)(poster ? 0.5 : 0.5 + shot.Camera.X),
                _size.Height * (float)(poster ? 0.5 : 0.48 + shot.Camera.Y + (shotIndex % 2 == 1 ? 0.025 : -0.025)));
            var revealDoneTime = tracking.Count == 0 ? shot.EndTime : tracking.Max(item => item[^1].StartTime);
            shots.Add(new ShotView(shot, shotRoot, glyphs, guides, frames, mg,
                poster ? Vector2.Zero : new Vector2(hero?.X ?? 0, hero?.Y ?? 0), basePosition,
                new TrackingFocusData(tracking), revealDoneTime));
        }
        _stage.Add(root);
        BringChromeToFront();
        var shotList = paragraph.Shots.ToArray();
        var transitionSeed = SonnetRandom.Hash($"{Program.Seed}:{paragraph.Id}:transition-frame");
        _cache[index] = new ParagraphView(paragraph, root, shots, sceneSeed, transitionSeed, shotList);
    }

    private void UpdateParagraph(ParagraphView paragraph, int paragraphIndex, double time)
    {
        Device.PostProcess.SonnetNoiseSeed = paragraph.NoiseSeed % 10000 / 10000f;
        var shotIndex = 0;
        for (var index = paragraph.Shots.Count - 1; index >= 0; index--)
            if (time >= paragraph.Shots[index].Shot.StartTime) { shotIndex = index; break; }
        var shotTransition = SonnetTransitions.ResolveShot(paragraph.ShotList, shotIndex, time,
            Tuning.EnableTransitions, paragraph.TransitionSeed, Tuning.EnableGlitchTransitions);
        var transitionsEnabled = Tuning.EnableTransitions && !Options.StaticMode;
        var previousTransition = paragraphIndex > 0 ? Program.Paragraphs[paragraphIndex - 1].TransitionOut : null;
        var enterDuration = previousTransition is null
            ? 0
            : Math.Clamp(previousTransition.EndTime - previousTransition.StartTime, 0.16, 0.3);
        var paragraphStart = paragraph.Paragraph.StartTime;
        var entering = transitionsEnabled && previousTransition is not null &&
            time >= paragraphStart && time <= paragraphStart + enterDuration;
        var paragraphTransition = entering
            ? SonnetTransitions.ResolveEnter(previousTransition!.Kind, time - paragraphStart, enterDuration,
                true, paragraph.TransitionSeed, Tuning.EnableGlitchTransitions)
            : SonnetTransitions.ResolveParagraph(paragraph.Paragraph, time, transitionsEnabled,
                paragraph.TransitionSeed, Tuning.EnableGlitchTransitions);
        var transition = shotTransition != SonnetMotion.IdleTransition ? shotTransition : paragraphTransition;
        var credits = ResolveCreditsFrame(time);
        var isFinal = paragraphIndex == Program.Paragraphs.Count - 1;
        paragraph.Root.Alpha = (float)(transition.Alpha * (isFinal && _credits is not null ? credits.LyricAlpha : 1));
        // Folia defocuses the final scene while the credits poster rises; blur the lyric
        // glyphs themselves so the poster above stays sharp.
        var lyricBlur = isFinal && _credits is not null ? (float)credits.LyricBlur * 0.5f : 0;
        foreach (var shot in paragraph.Shots)
            foreach (var glyph in shot.Glyphs)
                if (glyph.Core is { } core) core.Blur = lyricBlur;
        Device.PostProcess.Blur = (float)(transition.Blur / 14);
        Device.PostProcess.Glitch = (float)transition.Glitch;
        // Folia's glitch seed reaches ~4e5, which pushes the shader's sin() hash far past
        // float32 range so every slice gate reads the same. Wrap it in double precision first;
        // the 0.173 step between glitch frames still changes the tear pattern.
        Device.PostProcess.Seed = (float)(transition.GlitchSeed % 1024);

        for (var index = 0; index < paragraph.Shots.Count; index++)
        {
            var view = paragraph.Shots[index];
            view.Root.IsVisible = index == shotIndex;
            if (index != shotIndex) continue;
            ActiveShotKind = view.Shot.Kind;
#if DEBUG
            if (!ReferenceEquals(_debugShot, view.Shot))
            {
                _debugShot = view.Shot;
                var seed = unchecked(paragraph.NoiseSeed + (uint)(shotIndex * 97));
                var hasGeometry = view.Shot.Kind is SonnetShotKind.TypeImpact or SonnetShotKind.FragmentCollage;
                var geometry = !Tuning.ShowOnlyText && Tuning.ShowBackgroundMg && hasGeometry
                    ? $"G{seed % SonnetVariantResolver.GeometryVariantCount:D2}" : "G—";
                var background = !Tuning.ShowOnlyText && Tuning.ShowBackgroundMg
                    ? $"B{SonnetVariantResolver.Background(seed):D2}" : "B—";
                var fixedGeometry = !Tuning.ShowOnlyText && Tuning.ShowFixedGeo && hasGeometry
                    ? $"F{SonnetVariantResolver.FixedGeometry(seed):D2}" : "F—";
                var decor = !Tuning.ShowOnlyText && Tuning.ShowBackgroundDecor
                    ? $"D{SonnetVariantResolver.BackgroundDecor(seed):D2}" : "D—";
                ActivePresetDebugLabel = $"DEBUG  L{(int)view.Shot.Kind:D2}  {geometry}\n"
                    + $"{view.Shot.Kind}  |  {background}  {fixedGeometry}  {decor}\n"
                    + $"段落 {_activeParagraph + 1} / 镜头 {shotIndex + 1}  ·  预设编号从 0 开始";
            }
#endif
            UpdateShot(view, time);
        }
    }

    private void UpdateShot(ShotView view, double time)
    {
        var kind = view.Shot.Kind;
        var frame = SonnetMotion.ShotFrame(kind, SonnetMotion.ShotProgress(view.Shot, time));
        double frameX = frame.X, frameY = frame.Y, frameScale = frame.Scale, frameRotation = frame.Rotation;
        // Keep drifting through the gap after the shot ends, continuing the direction the
        // camera travelled over the last fifth of the shot, so the frame never freezes.
        var gapTime = Math.Max(0, time - view.Shot.EndTime);
        if (gapTime > 0)
        {
            var tail = SonnetMotion.ShotFrame(kind, 0.8);
            var drift = (1 - Math.Exp(-gapTime * 0.4)) * 2.0;
            frameX += (frame.X - tail.X) * drift;
            frameY += (frame.Y - tail.Y) * drift;
            frameScale += (frame.Scale - tail.Scale) * drift;
            frameRotation += (frame.Rotation - tail.Rotation) * drift;
        }
        var breathWeight = SonnetMotion.BreathWeight(time, view.RevealDoneTime);
        if (breathWeight > 0)
        {
            var phase = SonnetRandom.Hash(view.Shot.Id) % 1024 / 1024d * Math.PI * 2;
            var breath = SonnetMotion.CameraBreath(time, phase);
            frameX += breath.X * breathWeight;
            frameY += breath.Y * breathWeight;
            frameScale += breath.Scale * breathWeight;
            frameRotation += breath.Rotation * breathWeight;
        }
        var camera = Tuning.CameraIntensity * AnimationScale();
        var motion = Tuning.TypographyMotion * AnimationScale();
        var focus = ResolveTrackingFocus(view.TrackingFocus, time, view.Shot.StartTime, view.Shot.EndTime, view.Focus);
        view.Root.Pivot = Vector2.Lerp(view.Focus, focus, camera);
        view.Root.Scale = new Vector2((float)(view.Shot.Camera.Zoom * (1 + (frameScale - 1) * camera)));
        view.Root.Rotation = (float)((view.Shot.Camera.Rotation + frameRotation) * camera);
        var cameraOffset = new Vector2(_size.Width * (float)frameX, _size.Height * (float)frameY) * camera;
        view.Root.Position = view.BasePosition + cameraOffset;
        view.Mg.Update(time, view.Shot.StartTime, view.Shot.EndTime, Audio, cameraOffset,
            (float)frameScale, view.Root.Rotation);

        foreach (var decor in view.Frames)
        {
            decor.Root.IsVisible = Tuning.ShowFixedGeo && !Tuning.ShowOnlyText;
            decor.Update(time);
        }

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
            var offset = (float)((1 - glyphProgress) * motion);
            var coreAlpha = waiting ? 0 : (float)(0.16 + glyphProgress * 0.84);
            var scale = SonnetTypographyLayout.IsEmphasis(glyph.Role) && kind == SonnetShotKind.TypeImpact
                ? 0.52f + (float)glyphProgress * 0.48f
                : 0.86f + (float)glyphProgress * 0.14f;
            var isDecoration = glyph.Role == SonnetSegmentRole.Decoration;
            var visible = Tuning.ShowOnlyText
                ? glyph.IsTextGlyph && (!isDecoration || Tuning.ShowGiantDecorativeText)
                : (!glyph.IsBackgroundShape || Tuning.ShowBackgroundDecor) && (!isDecoration || Tuning.ShowGiantDecorativeText);
            // Simulated depth: far layers trail the camera and shrink, near ones lead and grow.
            var parallax = cameraOffset * glyph.Depth * 2.5f;
            var depthScale = 1 + glyph.Depth * 0.45f;
            var wrapper = glyph.Wrapper;
            wrapper.IsVisible = visible;
            wrapper.Alpha = coreAlpha;
            wrapper.Scale = new Vector2(scale * depthScale);
            wrapper.Position = glyph.Placement.Position + glyph.Placement.Entrance * offset + parallax;
            wrapper.Rotation = glyph.FinalRotation + glyph.Placement.EntryRotation * offset;

            if (glyph.CaWrapper is { } ca && glyph.Cyan is { } cyan && glyph.Red is { } red)
            {
                ca.IsVisible = visible && !Tuning.ShowOnlyText;
                ca.Alpha = coreAlpha;
                ca.Scale = wrapper.Scale;
                ca.Position = wrapper.Position;
                ca.Rotation = wrapper.Rotation;
                // Starts split on impact and merges down to a fifth of the offset.
                var caOffset = glyph.FontSize * (SonnetTypographyLayout.IsEmphasis(glyph.Role) ? 0.025f : 0.01f) *
                    (float)(1 - SonnetMotion.EaseInOut(glyphProgress) * 0.8);
                cyan.Position = new Vector2(-caOffset, caOffset * 0.5f);
                red.Position = new Vector2(caOffset, -caOffset * 0.5f);
            }

            glyph.Staff?.Update(time);
            if (glyph.Ghosts.Count > 0)
            {
                // Semi-hero echoes split along the layout normal on entry, then die fast.
                var ghostProgress = SonnetMotion.Clamp01((time - glyph.Placement.StartTime) / glyph.GhostDuration);
                var active = visible && ghostProgress > 0 && ghostProgress < 1;
                var envelope = ghostProgress <= 0.2
                    ? ghostProgress / 0.2
                    : Math.Pow(1 - (ghostProgress - 0.2) / 0.8, 2);
                var spread = (float)(1 - Math.Pow(1 - ghostProgress, 3));
                foreach (var ghost in glyph.Ghosts)
                {
                    ghost.Node.IsVisible = active;
                    if (!active) continue;
                    ghost.Node.Position = ghost.Offset * spread;
                    ghost.Node.Alpha = (float)envelope * ghost.AlphaBase;
                }
            }
        }
    }

    private TextNode Text(string text, float size, int weight, EffectColor color, EffectBlendMode blend = EffectBlendMode.Alpha) => new()
    {
        Text = text, FontFamily = Theme.FontFamily, FontSize = size, FontWeight = weight,
        Color = color, RasterScale = Tuning.TextureResolution, Anchor = new Vector2(0.5f), BlendMode = blend,
    };

    // Original Sonnet renders each glyph's glow and core inside Pixi's one TextStyle
    // texture. Rasterize both in a single pass so the layers share the same typeface,
    // metrics and baseline and can never spell different glyphs over each other.
    private TextNode GlowText(string text, float size, int weight,
        EffectColor coreColor, EffectColor glowColor)
    {
        var rasterScale = Math.Clamp(Tuning.TextureResolution, 1, 4);
        // Pixi renders the text shadow at roughly half of its `shadowBlur`, which the
        // original Sonnet sets to max(12, fontSize * 0.18) before DPR scaling. Keep a
        // slightly stronger fallback here so the glow stays visible on a cover layer.
        var sigma = MathF.Max(8f, size * 0.11f);
        return new TextNode
        {
            Text = text,
            FontFamily = Theme.FontFamily,
            FontSize = size,
            FontWeight = weight,
            Color = coreColor,
            GlowColor = glowColor,
            GlowSigma = sigma,
            RasterScale = rasterScale,
            Anchor = new Vector2(0.5f),
            BlendMode = EffectBlendMode.Alpha,
        };
    }

    private void ConfigurePostProcess(float time)
    {
        Device.PostProcess.Reset();
        Device.PostProcess.Time = time;
        Device.PostProcess.ResolutionScale = 1;
        Device.PostProcess.MultisampleCount = Tuning.MultisampleCount;
        Device.PostProcess.UseSonnetPasses = true;
        if (!Tuning.PostProcessEnabled || Options.StaticMode) return;
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
        Metadata = song.Metadata ?? new SonnetSongMetadata();
        ClearViews();
        RebuildChrome();
        if (_swapCover is not null)
        {
            _stage.Remove(_swapCover);
            _stage.Add(_swapCover);
        }
    }

    private SonnetCreditsFrame ResolveCreditsFrame(double time) =>
        SonnetCredits.Resolve(time, Program.Paragraphs.Count > 0 ? Program.Paragraphs[^1].EndTime : double.PositiveInfinity);

    private void UpdateCredits(double time)
    {
        if (_credits is null) return;
        var frame = ResolveCreditsFrame(time);
        _credits.IsVisible = frame.Active && !Tuning.ShowOnlyText;
        _credits.Alpha = (float)frame.PosterAlpha;
        var center = new Vector2(_size.Width, _size.Height) * 0.5f;
        _credits.Pivot = center;
        _credits.Position = center + new Vector2(0, (float)(frame.PosterOffsetY * _size.Height));
        _credits.Scale = new Vector2((float)frame.PosterScale);
    }

    /// <summary>Rebuilds the theme-dependent chrome (credits poster and outer frame) for the current size.</summary>
    private void RebuildChrome()
    {
        if (_credits is not null) _stage.Remove(_credits);
        if (_overlay is not null) _stage.Remove(_overlay);
        _credits = null;
        _overlay = null;
        if (_size.Width <= 0 || _size.Height <= 0 || Tuning.ShowOnlyText) return;
        if (SonnetCredits.HasMetadata(Metadata))
        {
            _credits = SonnetCreditsPoster.Build(Theme, Metadata, _size.Width, _size.Height,
                Options.LyricsFontScale, Tuning.TextureResolution);
            _credits.IsVisible = false;
        }
        if (Tuning.OuterFrameMode != SonnetOuterFrameMode.None)
            _overlay = SonnetMgBuilder.BuildOverlay(Theme, _size.Width, _size.Height);
        BringChromeToFront();
    }

    // Folia's stage order: scenes, then credits, then the outer frame, with the song-swap cover on top.
    private void BringChromeToFront()
    {
        foreach (var node in new EffectNode?[] { _credits, _overlay, _swapCover })
        {
            if (node is null) continue;
            if (node.Parent is not null) _stage.Remove(node);
            _stage.Add(node);
        }
    }

    private void RebuildSwapCover()
    {
        if (_swapCover is not null) _stage.Remove(_swapCover);
        _swapCover = new ShapeNode
        {
            Size = new Vector2(_size.Width, _size.Height),
            Color = Theme.Background,
            IsVisible = false,
        };
        _stage.Add(_swapCover);
    }

    private void Prune(int active)
    {
        _pruneBuffer.Clear();
        foreach (var index in _cache.Keys)
            if (Math.Abs(index - active) > 1)
                _pruneBuffer.Add(index);
        foreach (var index in _pruneBuffer)
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
#if DEBUG
        _debugShot = null;
        ActivePresetDebugLabel = null;
#endif
    }

    internal static Vector2 ResolveTrackingFocus(
        IReadOnlyList<IReadOnlyList<(Vector2 Position, double StartTime, bool IsBackgroundShape)>> segments,
        double time, double start, double end, Vector2 fallback)
    {
        if (segments.Count == 0) return fallback;
        return ResolveTrackingFocus(new TrackingFocusData(segments), time, start, end, fallback);
    }

    internal static Vector2 ResolveTrackingFocus(TrackingFocusData data, double time, double start, double end, Vector2 fallback)
    {
        if (data.Segments.Count == 0) return fallback;
        return SmoothedTrackingFocus(data, Math.Clamp(time, start, end), start, end);
    }

    private static Vector2 SampleTrackingFocus(TrackingFocusData data, double sampleTime)
    {
        if (data.Semantic.Length == 0) return Vector2.Zero;
        SonnetMotion.FillFocusWeights(data.Ranges, data.WeightsBuffer, sampleTime, 0.35);
        var position = Vector2.Zero;
        for (var index = 0; index < data.Semantic.Length; index++)
            position += SonnetMotion.SegmentCameraFocusCore(data.Semantic[index], sampleTime) * (float)data.WeightsBuffer[index];
        return position;
    }

    private static Vector2 SmoothedTrackingFocus(
        TrackingFocusData data, double time, double startTime, double endTime,
        double smoothingWindow = 0.12, double maxBlendDistance = 96)
    {
        var safeStart = Math.Min(startTime, endTime);
        var safeEnd = Math.Max(startTime, endTime);
        var radius = Math.Max(0, smoothingWindow);
        if (radius == 0 || safeStart == safeEnd)
            return SampleTrackingFocus(data, Math.Clamp(time, safeStart, safeEnd));

        ReadOnlySpan<(double Offset, double Weight)> kernel =
            [(-1, 1), (-0.5, 4), (0, 6), (0.5, 4), (1, 1)];
        Span<Vector2> samples = stackalloc Vector2[kernel.Length];
        for (var index = 0; index < kernel.Length; index++)
            samples[index] = SampleTrackingFocus(data, Math.Clamp(time + kernel[index].Offset * radius, safeStart, safeEnd));
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

    /// <summary>
    /// Per-shot tracking camera data precomputed at paragraph build time so the
    /// per-frame focus path (5 kernel samples, each reweighting all segments)
    /// runs without heap allocations.
    /// </summary>
    internal sealed class TrackingFocusData
    {
        public TrackingFocusData(IReadOnlyList<IReadOnlyList<(Vector2 Position, double StartTime, bool IsBackgroundShape)>> segments)
        {
            Segments = segments;
            Ranges = new (double Start, double End)[segments.Count];
            Semantic = new (Vector2 Position, double StartTime)[segments.Count][];
            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                Ranges[index] = (segment[0].StartTime, segment[^1].StartTime);
                var semanticCount = 0;
                for (var glyphIndex = 0; glyphIndex < segment.Count; glyphIndex++)
                    if (!segment[glyphIndex].IsBackgroundShape) semanticCount++;
                var semantic = new (Vector2 Position, double StartTime)[semanticCount];
                var fill = 0;
                for (var glyphIndex = 0; glyphIndex < segment.Count; glyphIndex++)
                {
                    var glyph = segment[glyphIndex];
                    if (!glyph.IsBackgroundShape) semantic[fill++] = (glyph.Position, glyph.StartTime);
                }
                Semantic[index] = semantic;
            }
            WeightsBuffer = new double[segments.Count];
        }

        public IReadOnlyList<IReadOnlyList<(Vector2 Position, double StartTime, bool IsBackgroundShape)>> Segments { get; }
        public (double Start, double End)[] Ranges { get; }
        public (Vector2 Position, double StartTime)[][] Semantic { get; }
        public double[] WeightsBuffer { get; }
    }

    private sealed record GlyphView(EffectContainer Wrapper, EffectContainer? CaWrapper, TextNode? Cyan, TextNode? Red,
        TextNode? Core, List<GhostView> Ghosts, SonnetGlyphPlacement Placement, SonnetSegmentRole Role,
        float FinalRotation, float FontSize, float Depth, double GhostDuration, bool IsBackgroundShape,
        SonnetStaffView? Staff = null)
    {
        public bool IsTextGlyph => !IsBackgroundShape && Staff is null;
    }
    private sealed record GhostView(TextNode Node, Vector2 Offset, float AlphaBase);
    private sealed record ShotView(SonnetShot Shot, EffectContainer Root, List<GlyphView> Glyphs,
        List<SonnetGuideView> Guides, List<SonnetFrameDecorView> Frames, SonnetMgView Mg, Vector2 Focus, Vector2 BasePosition,
        TrackingFocusData TrackingFocus, double RevealDoneTime);
    private sealed record ParagraphView(SonnetParagraph Paragraph, EffectContainer Root, List<ShotView> Shots,
        uint NoiseSeed, uint TransitionSeed, SonnetShot[] ShotList);
    private sealed record PendingSongSwap(SonnetSongContext Song, long StartedAt, bool Committed);
}
