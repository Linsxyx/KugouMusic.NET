using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using ZLinq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using SkiaSharp;

namespace KugouAvaloniaPlayer.Controls;

// Folia's Fume visualizer expressed as an immutable article layout plus a moving camera.
public sealed class FumeVisualizerControl : Control
{
    private const double CameraScaleMin = 0.22;
    private const double CameraScaleMax = 2.24;
    private static readonly TimeSpan LayoutRebuildDelay = TimeSpan.FromMilliseconds(96);

    public static readonly StyledProperty<PlayerViewModel?> PlayerProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, PlayerViewModel?>(nameof(Player));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, bool>(nameof(IsActive));

    public static readonly StyledProperty<FontFamily> LyricFontFamilyProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, FontFamily>(
            nameof(LyricFontFamily),
            FontFamily.Default);

    public static readonly StyledProperty<double> BackgroundObjectOpacityProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, double>(
            nameof(BackgroundObjectOpacity),
            0.5);

    public static readonly StyledProperty<double> TextHoldRatioProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, double>(nameof(TextHoldRatio), 1);

    public static readonly StyledProperty<FumeCameraTrackingMode> CameraTrackingModeProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, FumeCameraTrackingMode>(
            nameof(CameraTrackingMode),
            FumeCameraTrackingMode.Smooth);

    public static readonly StyledProperty<double> CameraSpeedProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, double>(nameof(CameraSpeed), 1);

    public static readonly StyledProperty<double> GlowIntensityProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, double>(nameof(GlowIntensity), 1);

    public static readonly StyledProperty<double> HeroScaleProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, double>(nameof(HeroScale), 1);

    private PlayerViewModel? _subscribedPlayer;
    private FumeArticleLayout? _article;
    private IReadOnlyList<FumeBackgroundShape> _backgroundShapes = [];
    private Size _layoutViewport;
    private string _layoutFont = string.Empty;
    private double _layoutHeroScale;
    private int _layoutLyricsSignature;
    private DateTimeOffset _layoutRebuildAt;
    private bool _layoutDirty = true;
    private bool _frameQueued;
    private bool _hasFrameTimestamp;
    private TimeSpan _lastFrameTimestamp;
    private int _settleFrames;

    private double _cameraX;
    private double _cameraY;
    private double _cameraScale = 1.18;
    private double _cameraVelocityX;
    private double _cameraVelocityY;
    private double _cameraVelocityScale;
    private int _cameraSourceIndex = int.MinValue;
    private double _retargetElapsed;
    private double _retargetDuration = 0.12;
    private double _retargetFromX;
    private double _retargetFromY;
    private double _retargetFromScale = 1.18;
    private bool _useOverviewBridge;
    private double _bridgeX;
    private double _bridgeY;
    private double _bridgeScale;

    static FumeVisualizerControl()
    {
        AffectsRender<FumeVisualizerControl>(
            PlayerProperty,
            IsActiveProperty,
            LyricFontFamilyProperty,
            BackgroundObjectOpacityProperty,
            TextHoldRatioProperty,
            CameraTrackingModeProperty,
            CameraSpeedProperty,
            GlowIntensityProperty,
            HeroScaleProperty);
    }

    public PlayerViewModel? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public FontFamily LyricFontFamily
    {
        get => GetValue(LyricFontFamilyProperty);
        set => SetValue(LyricFontFamilyProperty, value);
    }

    public double BackgroundObjectOpacity
    {
        get => GetValue(BackgroundObjectOpacityProperty);
        set => SetValue(BackgroundObjectOpacityProperty, value);
    }

    public double TextHoldRatio
    {
        get => GetValue(TextHoldRatioProperty);
        set => SetValue(TextHoldRatioProperty, value);
    }

    public FumeCameraTrackingMode CameraTrackingMode
    {
        get => GetValue(CameraTrackingModeProperty);
        set => SetValue(CameraTrackingModeProperty, value);
    }

    public double CameraSpeed
    {
        get => GetValue(CameraSpeedProperty);
        set => SetValue(CameraSpeedProperty, value);
    }

    public double GlowIntensity
    {
        get => GetValue(GlowIntensityProperty);
        set => SetValue(GlowIntensityProperty, value);
    }

    public double HeroScale
    {
        get => GetValue(HeroScaleProperty);
        set => SetValue(HeroScaleProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachPlayer(Player);
        MarkLayoutDirty(false);
        RequestNextFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AttachPlayer(null);
        _frameQueued = false;
        _hasFrameTimestamp = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayerProperty)
        {
            AttachPlayer(change.NewValue as PlayerViewModel);
            MarkLayoutDirty(false);
            ResetCamera();
        }
        else if (change.Property == BoundsProperty ||
                 change.Property == LyricFontFamilyProperty ||
                 change.Property == HeroScaleProperty)
        {
            MarkLayoutDirty(_article != null);
        }

        if (change.Property == IsActiveProperty && change.NewValue is true)
        {
            _hasFrameTimestamp = false;
            _settleFrames = 90;
            RequestNextFrame();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 1 || Bounds.Height <= 1 || Player == null)
            return;

        EnsureArticle();
        var article = _article;
        var player = Player;
        if (article == null)
        {
            context.Custom(new EmptyFumeDrawOperation(
                new Rect(Bounds.Size),
                ResolveEnergy(player),
                BackgroundObjectOpacity));
            return;
        }

        var currentSeconds = player.CurrentPositionSeconds;
        var overview = ShouldShowOverview(article, currentSeconds);
        var cameraTarget = ResolveCameraTarget(
            article,
            player.CurrentLyricIndex,
            currentSeconds,
            overview);
        if (!_hasFrameTimestamp)
            SnapCameraIfUninitialized(cameraTarget, article);

        context.Custom(new FumeDrawOperation(
            new Rect(Bounds.Size),
            new FumeFrame(
                article,
                _backgroundShapes,
                currentSeconds,
                _lastFrameTimestamp.TotalSeconds,
                _cameraX,
                _cameraY,
                _cameraScale,
                ResolveEnergy(player),
                Math.Clamp(BackgroundObjectOpacity, 0, 1),
                Math.Clamp(TextHoldRatio, 0, 1),
                Math.Clamp(GlowIntensity, 0, 1.8),
                LyricFontFamily.ToString(),
                overview)));
    }

    private void EnsureArticle()
    {
        var player = Player;
        if (player == null)
            return;

        var viewport = Bounds.Size;
        var font = LyricFontFamily.ToString();
        var signature = ComputeLyricsSignature(player);
        var heroScale = Math.Clamp(HeroScale, 0.82, 1.32);
        var layoutInputsChanged =
            viewport != _layoutViewport ||
            !string.Equals(font, _layoutFont, StringComparison.Ordinal) ||
            signature != _layoutLyricsSignature ||
            Math.Abs(heroScale - _layoutHeroScale) > 0.0001;
        if (layoutInputsChanged && !_layoutDirty)
        {
            // EnsureArticle can run inside Render. Updating the internal dirty state is
            // safe there, but invalidating the visual again would re-enter the render pass.
            // Only start the debounce window once; moving it on every animation frame can
            // postpone a lyric rebuild forever while playback keeps rendering.
            _layoutDirty = true;
            _layoutRebuildAt = _article != null
                ? DateTimeOffset.UtcNow + LayoutRebuildDelay
                : DateTimeOffset.MinValue;
        }

        if (!_layoutDirty ||
            _article != null && DateTimeOffset.UtcNow < _layoutRebuildAt)
            return;

        _layoutDirty = false;
        _layoutViewport = viewport;
        _layoutFont = font;
        _layoutLyricsSignature = signature;
        _layoutHeroScale = heroScale;
        _article = FumeArticleLayoutEngine.Build(
            player.RenderLyricLines,
            viewport.Width,
            viewport.Height,
            font,
            1,
            heroScale);
        _backgroundShapes = BuildBackgroundShapes(_article, viewport, signature);
        ResetCamera();
    }

    private void MarkLayoutDirty(bool debounce)
    {
        _layoutDirty = true;
        _layoutRebuildAt = debounce
            ? DateTimeOffset.UtcNow + LayoutRebuildDelay
            : DateTimeOffset.MinValue;
        _settleFrames = Math.Max(_settleFrames, 12);
        InvalidateVisual();
        RequestNextFrame();
    }

    private void AttachPlayer(PlayerViewModel? player)
    {
        if (ReferenceEquals(_subscribedPlayer, player))
            return;

        if (_subscribedPlayer != null)
        {
            _subscribedPlayer.PropertyChanged -= OnPlayerPropertyChanged;
            _subscribedPlayer.RenderLyricLines.CollectionChanged -= OnLyricLinesChanged;
            _subscribedPlayer.VisualizerUpdated -= OnVisualizerUpdated;
        }

        _subscribedPlayer = player;
        if (_subscribedPlayer == null)
            return;

        _subscribedPlayer.PropertyChanged += OnPlayerPropertyChanged;
        _subscribedPlayer.RenderLyricLines.CollectionChanged += OnLyricLinesChanged;
        _subscribedPlayer.VisualizerUpdated += OnVisualizerUpdated;
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.DisplayedPlayingSong))
        {
            MarkLayoutDirty(false);
            ResetCamera();
        }

        if (e.PropertyName is nameof(PlayerViewModel.CurrentPositionSeconds) or
            nameof(PlayerViewModel.CurrentLyricIndex) or
            nameof(PlayerViewModel.IsPlayingAudio))
        {
            _settleFrames = Math.Max(_settleFrames, 45);
            InvalidateVisual();
            RequestNextFrame();
        }
    }

    private void OnLyricLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MarkLayoutDirty(false);
    }

    private void OnVisualizerUpdated()
    {
        if (!IsActive)
            return;
        InvalidateVisual();
        RequestNextFrame();
    }

    private void RequestNextFrame()
    {
        if (_frameQueued || !ShouldAnimate())
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;
        _frameQueued = true;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private bool ShouldAnimate()
    {
        return IsActive &&
               IsVisible &&
               Bounds is { Width: > 1, Height: > 1 } &&
               TopLevel.GetTopLevel(this) != null &&
               (Player?.IsPlayingAudio == true || _settleFrames > 0 || _layoutDirty);
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        _frameQueued = false;
        if (!IsActive || Player == null)
        {
            _hasFrameTimestamp = false;
            return;
        }

        var deltaSeconds = _hasFrameTimestamp
            ? Math.Clamp((timestamp - _lastFrameTimestamp).TotalSeconds, 1d / 240d, 0.05d)
            : 1d / 60d;
        _hasFrameTimestamp = true;
        _lastFrameTimestamp = timestamp;

        EnsureArticle();
        if (_article != null)
        {
            var overview = ShouldShowOverview(_article, Player.CurrentPositionSeconds);
            var target = ResolveCameraTarget(
                _article,
                Player.CurrentLyricIndex,
                Player.CurrentPositionSeconds,
                overview);
            UpdateCamera(target, deltaSeconds);
        }

        if (Player.IsPlayingAudio != true && _settleFrames > 0)
            _settleFrames--;
        InvalidateVisual();
        RequestNextFrame();
    }

    private CameraTarget ResolveCameraTarget(
        FumeArticleLayout article,
        int currentLineIndex,
        double currentSeconds,
        bool overview)
    {
        if (overview)
            return ResolveOverviewTarget(article);

        var block = ResolveFocusBlock(article, currentLineIndex, currentSeconds);
        if (block == null)
            return new CameraTarget(article.Width * 0.5, article.Height * 0.5, 1.18, -1);

        var printed = ResolvePrintedProgress(block, currentSeconds);
        if (CameraTrackingMode == FumeCameraTrackingMode.Stepped)
            printed = Math.Floor(printed);
        var point = ResolveFocusPoint(block, printed);
        var targetLineHeight = Math.Clamp(Math.Min(Bounds.Width, Bounds.Height) * 0.115, 64, 124);
        var scale = Math.Clamp(targetLineHeight / Math.Max(block.LineHeight, 1), 0.88, 2.2);
        return new CameraTarget(point.X, point.Y, scale, block.SourceLineIndex);
    }

    private void UpdateCamera(CameraTarget target, double deltaSeconds)
    {
        var speed = Math.Clamp(CameraSpeed, 0.55, 1.85);
        if (_cameraSourceIndex != target.SourceIndex)
        {
            _cameraSourceIndex = target.SourceIndex;
            _retargetElapsed = 0;
            _retargetFromX = _cameraX;
            _retargetFromY = _cameraY;
            _retargetFromScale = _cameraScale;
            var deltaX = target.X - _cameraX;
            var deltaY = target.Y - _cameraY;
            var screenDistance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY) *
                                 Math.Max(_cameraScale, target.Scale);
            var minimumSide = Math.Max(Math.Min(Bounds.Width, Bounds.Height), 1);
            _useOverviewBridge = target.SourceIndex >= 0 && screenDistance >= minimumSide * 2.75;
            _retargetDuration = _useOverviewBridge
                ? Math.Clamp((0.3 + screenDistance / minimumSide / 18) / speed, 0.22, 0.9)
                : Math.Clamp(0.1 / speed, 0.03, 0.3);

            var overview = _article == null ? target : ResolveOverviewTarget(_article);
            _bridgeX = Mix((_cameraX + target.X) * 0.5, overview.X, 0.32);
            _bridgeY = Mix((_cameraY + target.Y) * 0.5, overview.Y, 0.32);
            _bridgeScale = Math.Clamp(
                Math.Max(overview.Scale * 1.65, Math.Max(_cameraScale, target.Scale) * 0.48),
                CameraScaleMin,
                Math.Max(_cameraScale, target.Scale));
        }

        _retargetElapsed += deltaSeconds;
        var phase = Math.Clamp(_retargetElapsed / Math.Max(_retargetDuration, 0.001), 0, 1);
        if (_useOverviewBridge && phase < 1)
        {
            var eased = EaseOutCubic(phase);
            var x = Quadratic(_retargetFromX, _bridgeX, target.X, eased);
            var y = Quadratic(_retargetFromY, _bridgeY, target.Y, eased);
            var scale = Quadratic(_retargetFromScale, _bridgeScale, target.Scale, eased);
            var catchUp = 1 - Math.Exp(-deltaSeconds * Mix(12.5, 22, 1 - phase));
            _cameraX += (x - _cameraX) * catchUp;
            _cameraY += (y - _cameraY) * catchUp;
            _cameraScale += (scale - _cameraScale) * catchUp;
            _cameraVelocityX *= 0.72;
            _cameraVelocityY *= 0.72;
            _cameraVelocityScale *= 0.68;
            return;
        }

        var retargetBoost = 1 - EaseOutCubic(phase);
        var spring = Mix(208, 520, retargetBoost);
        var damping = Mix(24, 34, retargetBoost);
        _cameraVelocityX += ((target.X - _cameraX) * spring - _cameraVelocityX * damping) * deltaSeconds;
        _cameraVelocityY += ((target.Y - _cameraY) * spring - _cameraVelocityY * damping) * deltaSeconds;
        var maxVelocity = Mix(1320, 6400, retargetBoost);
        _cameraVelocityX = Math.Clamp(_cameraVelocityX, -maxVelocity, maxVelocity);
        _cameraVelocityY = Math.Clamp(_cameraVelocityY, -maxVelocity, maxVelocity);
        _cameraX += _cameraVelocityX * deltaSeconds;
        _cameraY += _cameraVelocityY * deltaSeconds;

        var scaleSpring = Mix(54, 108, retargetBoost);
        var scaleDamping = Mix(13.5, 21, retargetBoost);
        _cameraVelocityScale +=
            ((target.Scale - _cameraScale) * scaleSpring - _cameraVelocityScale * scaleDamping) *
            deltaSeconds;
        _cameraVelocityScale = Math.Clamp(_cameraVelocityScale, -1.6, 1.6);
        _cameraScale = Math.Clamp(
            _cameraScale + _cameraVelocityScale * deltaSeconds,
            CameraScaleMin,
            CameraScaleMax);
    }

    private void SnapCameraIfUninitialized(CameraTarget target, FumeArticleLayout article)
    {
        if (_cameraSourceIndex != int.MinValue)
            return;
        _cameraX = article.Width * 0.5;
        _cameraY = article.Height * 0.5;
        _cameraScale = 1.18;
        _cameraSourceIndex = target.SourceIndex - 1;
    }

    private void ResetCamera()
    {
        _cameraSourceIndex = int.MinValue;
        _cameraVelocityX = 0;
        _cameraVelocityY = 0;
        _cameraVelocityScale = 0;
        _hasFrameTimestamp = false;
        _settleFrames = 90;
    }

    private CameraTarget ResolveOverviewTarget(FumeArticleLayout article)
    {
        if (article.Blocks.Count == 0)
            return new CameraTarget(article.Width * 0.5, article.Height * 0.5, 0.4, -2);

        var minX = article.Blocks.AsValueEnumerable().Min(block => block.X);
        var minY = article.Blocks.AsValueEnumerable().Min(block => block.Y);
        var maxX = article.Blocks.AsValueEnumerable().Max(block => block.X + block.Width);
        var maxY = article.Blocks.AsValueEnumerable().Max(block => block.Y + block.Height);
        var paddingX = Math.Clamp(Bounds.Width * 0.2, 120, 280);
        var paddingY = Math.Clamp(Bounds.Height * 0.2, 96, 220);
        var scale = Math.Min(
            Bounds.Width / Math.Max(maxX - minX + paddingX * 2, 1),
            Bounds.Height / Math.Max(maxY - minY + paddingY * 2, 1));
        return new CameraTarget(
            (minX + maxX) * 0.5,
            (minY + maxY) * 0.5,
            Math.Clamp(scale, CameraScaleMin, 0.72),
            -2);
    }

    private static FumeArticleBlock? ResolveFocusBlock(
        FumeArticleLayout article,
        int currentLineIndex,
        double currentSeconds)
    {
        if (currentLineIndex >= 0 &&
            article.BlocksBySourceIndex.TryGetValue(currentLineIndex, out var active))
            return active;

        if (currentSeconds >= article.LastEndSeconds)
            return article.ChronologicalBlocks.AsValueEnumerable().LastOrDefault();

        for (var index = article.ChronologicalBlocks.Count - 1; index >= 0; index--)
        {
            var block = article.ChronologicalBlocks[index];
            if (ResolvePrintedProgress(block, currentSeconds) > 0)
                return block;
        }

        return article.ChronologicalBlocks.AsValueEnumerable().FirstOrDefault();
    }

    private static Point ResolveFocusPoint(FumeArticleBlock block, double printedProgress)
    {
        var offset = Math.Clamp(printedProgress, 0, block.Graphemes.Count);
        var targetLineIndex = 0;
        for (var index = 0; index < block.RenderLines.Count; index++)
        {
            targetLineIndex = index;
            if (offset <= block.RenderLines[index].End)
                break;
        }

        var line = block.RenderLines[targetLineIndex];
        var baseOffset = Math.Clamp((int)Math.Floor(offset), line.Start, line.End);
        var fraction = offset - Math.Floor(offset);
        var x = block.X + block.GlyphOffsets[baseOffset] - block.GlyphOffsets[line.Start];
        if (baseOffset < block.Graphemes.Count)
            x += (block.GlyphOffsets[baseOffset + 1] - block.GlyphOffsets[baseOffset]) * fraction;
        return new Point(
            Math.Clamp(x, block.X, block.X + line.Width),
            block.Y + targetLineIndex * block.LineHeight + block.LineHeight * 0.5);
    }

    private static double ResolvePrintedProgress(FumeArticleBlock block, double currentSeconds)
    {
        var lineStart = block.Line.Start.TotalSeconds;
        var lineEnd = lineStart + Math.Max(block.Line.Duration.TotalSeconds, 0.12);
        if (currentSeconds < lineStart)
            return 0;
        if (currentSeconds >= lineEnd)
            return block.Graphemes.Count;

        if (!HasTimedWordRanges(block))
            return Math.Clamp((currentSeconds - lineStart) / (lineEnd - lineStart), 0, 1) *
                   block.Graphemes.Count;

        var printed = 0d;
        foreach (var range in block.WordRanges)
        {
            if (range.End <= range.Start || range.EndSeconds <= range.StartSeconds)
                continue;
            if (currentSeconds < range.StartSeconds)
                return printed;
            var duration = range.EndSeconds - range.StartSeconds;
            var progress = Math.Clamp((currentSeconds - range.StartSeconds) / duration, 0, 1);
            printed = range.Start + (range.End - range.Start) * progress;
            if (progress < 1)
                return printed;
        }

        return Math.Clamp(printed, 0, block.Graphemes.Count);
    }

    private static bool HasTimedWordRanges(FumeArticleBlock block)
    {
        foreach (var range in block.WordRanges)
        {
            if (range.End > range.Start && range.EndSeconds > range.StartSeconds)
                return true;
        }

        return false;
    }

    private static bool ShouldShowOverview(FumeArticleLayout article, double currentSeconds)
    {
        var last = article.ChronologicalBlocks.AsValueEnumerable().LastOrDefault();
        if (last == null)
            return false;
        var start = last.Line.Start.TotalSeconds;
        return currentSeconds >= start + Math.Max(last.Line.Duration.TotalSeconds, 0) * 0.5;
    }

    private static int ComputeLyricsSignature(PlayerViewModel player)
    {
        var hash = new HashCode();
        hash.Add(player.RenderLyricLines.Count);
        foreach (var line in player.RenderLyricLines)
        {
            hash.Add(line.Text);
            hash.Add(line.Start);
            hash.Add(line.Duration);
            hash.Add(line.Words.Count);
        }
        return hash.ToHashCode();
    }

    private static FumeAudioEnergy ResolveEnergy(PlayerViewModel player)
    {
        var bars = player.NowPlayingVisualizerBars;
        if (bars.Length == 0)
            return default;

        static double Average(IReadOnlyList<VisualizerBandState> values, int start, int end)
        {
            var total = 0d;
            var count = 0;
            for (var index = start; index < end && index < values.Count; index++)
            {
                total += Math.Clamp((values[index].Height - 6) / 170, 0, 1);
                count++;
            }
            return count == 0 ? 0 : total / count;
        }

        var length = bars.Length;
        return new FumeAudioEnergy(
            Average(bars, 0, length / 5),
            Average(bars, length / 5, length * 2 / 5),
            Average(bars, length * 2 / 5, length * 3 / 5),
            Average(bars, length * 3 / 5, length * 4 / 5),
            Average(bars, length * 4 / 5, length));
    }

    private static IReadOnlyList<FumeBackgroundShape> BuildBackgroundShapes(
        FumeArticleLayout? article,
        Size viewport,
        int seed)
    {
        var worldWidth = Math.Max(article?.Width ?? viewport.Width * 1.8, viewport.Width * 1.2);
        var worldHeight = Math.Max(article?.Height ?? viewport.Height * 1.8, viewport.Height * 1.2);
        var paper = article?.PaperBounds ??
                    new FumePaperBounds(
                        worldWidth * 0.24,
                        worldHeight * 0.18,
                        worldWidth * 0.76,
                        worldHeight * 0.82);
        var baseUnit = Math.Clamp(Math.Min(viewport.Width, viewport.Height) * 0.72, 320, 760);
        var result = new List<FumeBackgroundShape>();
        for (var index = 0; index < 8; index++)
        {
            var localSeed = HashCode.Combine(seed, index, 113);
            var side = Math.Abs(localSeed) % 4;
            var size = baseUnit * StableMix(localSeed, 0.82, 1.36);
            var x = side switch
            {
                0 => paper.Left - size * 0.2,
                1 => paper.Right + size * 0.2,
                _ => StableMix(localSeed + 11, paper.Left, paper.Right)
            };
            var y = side switch
            {
                2 => paper.Top - size * 0.2,
                3 => paper.Bottom + size * 0.2,
                _ => StableMix(localSeed + 23, paper.Top, paper.Bottom)
            };
            result.Add(new FumeBackgroundShape(
                (FumeShapeKind)(index % 3),
                x,
                y,
                size,
                StableMix(localSeed + 31, -0.6, 0.6),
                StableMix(localSeed + 47, -0.045, 0.045),
                StableMix(localSeed + 59, 0.035, 0.16),
                StableMix(localSeed + 71, 0, 1),
                index % 5));
        }

        for (var index = 0; index < 12; index++)
        {
            var localSeed = HashCode.Combine(seed, index, 877);
            result.Add(new FumeBackgroundShape(
                FumeShapeKind.Spark,
                StableMix(localSeed + 5, worldWidth * 0.2, worldWidth * 0.8),
                StableMix(localSeed + 7, worldHeight * 0.18, worldHeight * 0.82),
                baseUnit * StableMix(localSeed + 13, 0.1, 0.24),
                StableMix(localSeed + 17, -Math.PI, Math.PI),
                StableMix(localSeed + 19, -0.18, 0.18),
                StableMix(localSeed + 29, 0.08, 0.22),
                StableMix(localSeed + 37, 0, 1),
                index % 5));
        }

        return result.AsValueEnumerable().OrderBy(shape => shape.Depth).ToArray();
    }

    private static double StableMix(int seed, double min, double max)
    {
        var value = (uint)seed;
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        return min + (max - min) * (value % 10000 / 10000d);
    }

    private static double Mix(double from, double to, double amount) =>
        from + (to - from) * amount;

    private static double EaseOutCubic(double value) =>
        1 - Math.Pow(1 - Math.Clamp(value, 0, 1), 3);

    private static double Quadratic(double from, double control, double to, double amount)
    {
        var normalized = Math.Clamp(amount, 0, 1);
        var inverse = 1 - normalized;
        return inverse * inverse * from +
               2 * inverse * normalized * control +
               normalized * normalized * to;
    }

    private readonly record struct CameraTarget(double X, double Y, double Scale, int SourceIndex);
}

internal readonly record struct FumeAudioEnergy(
    double Bass,
    double LowMid,
    double Mid,
    double Vocal,
    double Treble)
{
    public double At(int index) => index switch
    {
        0 => Bass,
        1 => LowMid,
        2 => Mid,
        3 => Vocal,
        _ => Treble
    };
}

internal enum FumeShapeKind
{
    Ring,
    Square,
    Cross,
    Spark
}

internal readonly record struct FumeBackgroundShape(
    FumeShapeKind Kind,
    double X,
    double Y,
    double Size,
    double Rotation,
    double RotationSpeed,
    double Opacity,
    double Depth,
    int AudioBand);

internal sealed record FumeFrame(
    FumeArticleLayout Article,
    IReadOnlyList<FumeBackgroundShape> BackgroundShapes,
    double PlaybackSeconds,
    double ClockSeconds,
    double CameraX,
    double CameraY,
    double CameraScale,
    FumeAudioEnergy Energy,
    double BackgroundObjectOpacity,
    double TextHoldRatio,
    double GlowIntensity,
    string FontFamilyName,
    bool IsOverview);

internal sealed class FumeDrawOperation(Rect bounds, FumeFrame frame) : ICustomDrawOperation
{
    private static readonly SKColor Primary = new(242, 235, 221);
    private static readonly SKColor Accent = new(214, 169, 31);
    private static readonly SKColor Secondary = new(98, 126, 145);

    public Rect Bounds { get; } = bounds;

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature == null)
            return;

        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));

        DrawBackground(canvas);

        canvas.Save();
        canvas.Translate((float)(Bounds.Width * 0.5), (float)(Bounds.Height * 0.5));
        canvas.Scale((float)frame.CameraScale);
        canvas.Translate((float)-frame.CameraX, (float)-frame.CameraY);
        DrawArticle(canvas);
        canvas.Restore();
        canvas.Restore();
    }

    public bool HitTest(Point p) => false;

    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose()
    {
    }

    private void DrawBackground(SKCanvas canvas)
    {
        var backgroundCenterX = frame.Article.Width * 0.5;
        var backgroundCenterY = frame.Article.Height * 0.5;
        var cameraX = Mix(backgroundCenterX, frame.CameraX, 0.9);
        var cameraY = Mix(backgroundCenterY, frame.CameraY, 0.74) -
                      Math.Clamp(Bounds.Height * 0.22 / Math.Max(frame.CameraScale, 0.001), 48, 180);
        var scale = Math.Clamp(frame.CameraScale * 0.94, 0.22, 2.24);

        canvas.Save();
        canvas.Translate((float)(Bounds.Width * 0.5), (float)(Bounds.Height * 0.5));
        canvas.Scale((float)scale);
        canvas.Translate((float)-cameraX, (float)-cameraY);

        foreach (var shape in frame.BackgroundShapes)
            DrawShape(canvas, shape, cameraX, cameraY);
        canvas.Restore();
    }

    private void DrawShape(SKCanvas canvas, FumeBackgroundShape shape, double cameraX, double cameraY)
    {
        var band = frame.Energy.At(shape.AudioBand);
        var audioScale = Mix(0.95, 1.45, Math.Clamp(band, 0, 1));
        var opacityBoost = Mix(0.85, 1.55, Math.Clamp(band, 0, 1));
        var layerResponse = Mix(0.58, 1.16, shape.Depth);
        var x = shape.X + (cameraX - frame.Article.Width * 0.5) * (1 - layerResponse) * 0.72;
        var y = shape.Y + (cameraY - frame.Article.Height * 0.5) * (1 - layerResponse) * 0.72;
        var size = shape.Size * audioScale;
        var opacity = Math.Clamp(
            shape.Opacity * opacityBoost * frame.BackgroundObjectOpacity * 2,
            0,
            0.42);
        var color = shape.Kind is FumeShapeKind.Square or FumeShapeKind.Spark ? Accent : Secondary;

        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = shape.Kind == FumeShapeKind.Spark ? 1.15f : 1.05f;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.Color = WithAlpha(color, opacity);
        if (shape.Kind == FumeShapeKind.Spark)
            paint.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(3.5 * audioScale));

        canvas.Save();
        canvas.Translate((float)x, (float)y);
        canvas.RotateRadians((float)(shape.Rotation + frame.ClockSeconds * shape.RotationSpeed));
        var half = (float)(size * 0.5);
        switch (shape.Kind)
        {
            case FumeShapeKind.Ring:
                canvas.DrawArc(new SKRect(-half, -half, half, half), 18, 318, false, paint);
                break;
            case FumeShapeKind.Square:
                canvas.DrawRect(new SKRect(-half, -half, half, half), paint);
                break;
            case FumeShapeKind.Cross:
                DrawCross(canvas, half, paint);
                break;
            case FumeShapeKind.Spark:
                DrawSpark(canvas, half, paint);
                break;
        }
        canvas.Restore();
    }

    private void DrawArticle(SKCanvas canvas)
    {
        foreach (var block in frame.Article.Blocks)
        {
            if (!IsVisible(block))
                continue;
            DrawBlock(canvas, block);
        }
    }

    private bool IsVisible(FumeArticleBlock block)
    {
        const double overscan = 180;
        var left = Bounds.Width * 0.5 + (block.X - frame.CameraX) * frame.CameraScale;
        var top = Bounds.Height * 0.5 + (block.Y - frame.CameraY) * frame.CameraScale;
        var right = left + block.Width * frame.CameraScale;
        var bottom = top + block.Height * frame.CameraScale;
        return right >= -overscan &&
               left <= Bounds.Width + overscan &&
               bottom >= -overscan &&
               top <= Bounds.Height + overscan;
    }

    private void DrawBlock(SKCanvas canvas, FumeArticleBlock block)
    {
        using var requestedTypeface = SKTypeface.FromFamilyName(
            block.TypefaceFamily,
            block.IsHero ? SKFontStyleWeight.SemiBold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
        var typeface = requestedTypeface ?? SKTypeface.Default;
        using var font = new SKFont(typeface, (float)block.FontSize);
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        var lineStart = block.Line.Start.TotalSeconds;
        var lineEnd = lineStart + Math.Max(block.Line.Duration.TotalSeconds, 0.12);
        var lineDuration = Math.Max(lineEnd - lineStart, 0.18);
        var trailDuration = Math.Clamp(lineDuration * (block.IsHero ? 0.42 : 0.52), 0.45, 1.45);
        var waitingOpacity = block.IsHero ? 0.06 : 0.035;
        var activeOpacity = block.IsHero ? 0.985 : 0.92;
        var passedOpacity = block.IsHero ? 0.74 : 0.58;

        if (frame.PlaybackSeconds < lineStart)
        {
            DrawStaticBlock(canvas, block, font, paint, WithAlpha(Primary, waitingOpacity));
            return;
        }

        if (frame.PlaybackSeconds >= lineEnd + trailDuration)
        {
            var opacity = passedOpacity;
            if (frame is { TextHoldRatio: < 1, IsOverview: false })
            {
                var totalDuration = Math.Clamp(
                    (frame.Article.LastEndSeconds - frame.Article.FirstStartSeconds) * frame.TextHoldRatio,
                    2.4,
                    130);
                var dim = EaseInCubic(Math.Clamp(
                    (frame.PlaybackSeconds - lineEnd - trailDuration) / totalDuration,
                    0,
                    1));
                opacity = Mix(passedOpacity, block.IsHero ? 0.11 : 0.075, dim);
            }
            DrawStaticBlock(canvas, block, font, paint, WithAlpha(Primary, opacity));
            return;
        }

        var printedProgress = ResolvePrintedProgress(block);
        var hasTimedWords = HasTimedWordRanges(block);
        for (var lineIndex = 0; lineIndex < block.RenderLines.Count; lineIndex++)
        {
            var renderLine = block.RenderLines[lineIndex];
            var baseline = block.Y + lineIndex * block.LineHeight + block.LineHeight * 0.78;
            for (var glyphIndex = renderLine.Start; glyphIndex < renderLine.End; glyphIndex++)
            {
                var glyph = block.Graphemes[glyphIndex];
                if (string.IsNullOrEmpty(glyph))
                    continue;
                var x = block.X + block.GlyphOffsets[glyphIndex] - block.GlyphOffsets[renderLine.Start];
                var rangeIndex = glyphIndex < block.WordRangeByGlyph.Count
                    ? block.WordRangeByGlyph[glyphIndex]
                    : -1;
                ResolveGlyphTiming(
                    block,
                    glyphIndex,
                    hasTimedWords ? rangeIndex : -1,
                    out var glyphStart,
                    out var glyphEnd);
                var glyphDuration = Math.Max(glyphEnd - glyphStart, 0.001);
                var glyphProgress = Math.Clamp(
                    (frame.PlaybackSeconds - glyphStart) / glyphDuration,
                    0,
                    1);
                var trailStart = glyphStart + glyphDuration * 0.18;
                var trail = Math.Pow(Math.Clamp(
                    (frame.PlaybackSeconds - trailStart) / trailDuration,
                    0,
                    1), 1.35);

                paint.Color = WithAlpha(Primary, waitingOpacity);
                paint.MaskFilter = null;
                canvas.DrawText(glyph, (float)x, (float)baseline, font, paint);

                var playedFraction = ResolvePlayedFraction(
                    block,
                    glyphIndex,
                    hasTimedWords ? rangeIndex : -1,
                    printedProgress);
                if (playedFraction <= 0)
                    continue;

                var color = MixColor(Accent, Primary, 0.18 + trail * 0.82);
                var glyphWidth = Math.Max(
                    block.GlyphOffsets[glyphIndex + 1] - block.GlyphOffsets[glyphIndex],
                    block.FontSize * 0.08);

                if (frame.GlowIntensity > 0)
                {
                    using var glowPaint = new SKPaint();
                    glowPaint.IsAntialias = true;
                    glowPaint.Color = WithAlpha(
                        color,
                        (0.36 + glyphProgress * 0.36) *
                        EaseOutCubic(playedFraction) *
                        (1 - trail * 0.55));
                    glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                        SKBlurStyle.Normal,
                        (float)((3 + block.FontSize * 0.12) * frame.GlowIntensity));
                    canvas.DrawText(glyph, (float)x, (float)baseline, font, glowPaint);
                }

                var saveCount = canvas.Save();
                canvas.ClipRect(new SKRect(
                    (float)x,
                    (float)(baseline - block.LineHeight),
                    (float)(x + glyphWidth * playedFraction),
                    (float)(baseline + block.LineHeight * 0.25)));
                paint.Color = WithAlpha(color, activeOpacity);
                paint.MaskFilter = null;
                canvas.DrawText(glyph, (float)x, (float)baseline, font, paint);
                canvas.RestoreToCount(saveCount);

            }
        }
    }

    private static void DrawStaticBlock(
        SKCanvas canvas,
        FumeArticleBlock block,
        SKFont font,
        SKPaint paint,
        SKColor color)
    {
        paint.Color = color;
        paint.MaskFilter = null;
        for (var lineIndex = 0; lineIndex < block.RenderLines.Count; lineIndex++)
        {
            var line = block.RenderLines[lineIndex];
            var baseline = block.Y + lineIndex * block.LineHeight + block.LineHeight * 0.78;
            for (var glyphIndex = line.Start; glyphIndex < line.End; glyphIndex++)
            {
                var x = block.X + block.GlyphOffsets[glyphIndex] - block.GlyphOffsets[line.Start];
                canvas.DrawText(block.Graphemes[glyphIndex], (float)x, (float)baseline, font, paint);
            }
        }
    }

    private double ResolvePrintedProgress(FumeArticleBlock block)
    {
        var start = block.Line.Start.TotalSeconds;
        var end = start + Math.Max(block.Line.Duration.TotalSeconds, 0.12);
        if (frame.PlaybackSeconds <= start)
            return 0;
        if (frame.PlaybackSeconds >= end)
            return block.Graphemes.Count;
        if (!HasTimedWordRanges(block))
            return Math.Clamp((frame.PlaybackSeconds - start) / (end - start), 0, 1) *
                   block.Graphemes.Count;

        var printed = 0d;
        foreach (var range in block.WordRanges)
        {
            if (range.End <= range.Start || range.EndSeconds <= range.StartSeconds)
                continue;
            if (frame.PlaybackSeconds < range.StartSeconds)
                return printed;
            var progress = Math.Clamp(
                (frame.PlaybackSeconds - range.StartSeconds) /
                (range.EndSeconds - range.StartSeconds),
                0,
                1);
            printed = range.Start + (range.End - range.Start) * progress;
            if (progress < 1)
                return printed;
        }
        return printed;
    }

    private static bool HasTimedWordRanges(FumeArticleBlock block)
    {
        foreach (var range in block.WordRanges)
        {
            if (range.End > range.Start && range.EndSeconds > range.StartSeconds)
                return true;
        }

        return false;
    }

    private double ResolvePlayedFraction(
        FumeArticleBlock block,
        int glyphIndex,
        int rangeIndex,
        double printedProgress)
    {
        if (rangeIndex < 0 ||
            rangeIndex >= block.WordRanges.Count ||
            block.WordRanges[rangeIndex].EndSeconds <= block.WordRanges[rangeIndex].StartSeconds)
        {
            return Math.Clamp(printedProgress - glyphIndex, 0, 1);
        }

        var range = block.WordRanges[rangeIndex];
        var rangeStart = block.GlyphOffsets[range.Start];
        var rangeEnd = block.GlyphOffsets[range.End];
        var rangeWidth = Math.Max(rangeEnd - rangeStart, 0.001);
        var rangeProgress = Math.Clamp(
            (frame.PlaybackSeconds - range.StartSeconds) /
            (range.EndSeconds - range.StartSeconds),
            0,
            1);
        var playedOffset = rangeStart + rangeWidth * rangeProgress;
        var glyphStart = block.GlyphOffsets[glyphIndex];
        var glyphEnd = block.GlyphOffsets[glyphIndex + 1];
        return Math.Clamp(
            (playedOffset - glyphStart) / Math.Max(glyphEnd - glyphStart, 0.001),
            0,
            1);
    }

    private static void ResolveGlyphTiming(
        FumeArticleBlock block,
        int glyphIndex,
        int rangeIndex,
        out double start,
        out double end)
    {
        if (rangeIndex < 0 || rangeIndex >= block.WordRanges.Count)
        {
            var lineStart = block.Line.Start.TotalSeconds;
            var lineDuration = Math.Max(block.Line.Duration.TotalSeconds, 0.12);
            var count = Math.Max(block.Graphemes.Count, 1);
            start = lineStart + glyphIndex / (double)count * lineDuration;
            end = lineStart + (glyphIndex + 1d) / count * lineDuration;
            return;
        }

        var range = block.WordRanges[rangeIndex];
        var duration = Math.Max(range.EndSeconds - range.StartSeconds, 0.08);
        var rangeStart = block.GlyphOffsets[range.Start];
        var rangeWidth = Math.Max(block.GlyphOffsets[range.End] - rangeStart, 0.001);
        var glyphStart = block.GlyphOffsets[glyphIndex];
        var glyphEnd = block.GlyphOffsets[glyphIndex + 1];
        start = range.StartSeconds + (glyphStart - rangeStart) / rangeWidth * duration;
        end = range.StartSeconds + (glyphEnd - rangeStart) / rangeWidth * duration;
    }

    private static void DrawCross(SKCanvas canvas, float half, SKPaint paint)
    {
        var arm = half * 0.6f;
        using var path = new SKPath();
        path.MoveTo(-arm, -half);
        path.LineTo(arm, -half);
        path.LineTo(arm, -arm);
        path.LineTo(half, -arm);
        path.LineTo(half, arm);
        path.LineTo(arm, arm);
        path.LineTo(arm, half);
        path.LineTo(-arm, half);
        path.LineTo(-arm, arm);
        path.LineTo(-half, arm);
        path.LineTo(-half, -arm);
        path.LineTo(-arm, -arm);
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private static void DrawSpark(SKCanvas canvas, float half, SKPaint paint)
    {
        var inner = half * 0.26f;
        using var path = new SKPath();
        path.MoveTo(0, -half);
        path.LineTo(inner, -inner);
        path.LineTo(half, 0);
        path.LineTo(inner, inner);
        path.LineTo(0, half);
        path.LineTo(-inner, inner);
        path.LineTo(-half, 0);
        path.LineTo(-inner, -inner);
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private static SKColor WithAlpha(SKColor color, double opacity) =>
        color.WithAlpha((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255));

    private static SKColor MixColor(SKColor from, SKColor to, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return new SKColor(
            (byte)Math.Round(Mix(from.Red, to.Red, t)),
            (byte)Math.Round(Mix(from.Green, to.Green, t)),
            (byte)Math.Round(Mix(from.Blue, to.Blue, t)));
    }

    private static double Mix(double from, double to, double amount) =>
        from + (to - from) * amount;

    private static double EaseOutCubic(double value) =>
        1 - Math.Pow(1 - Math.Clamp(value, 0, 1), 3);

    private static double EaseInCubic(double value) =>
        Math.Pow(Math.Clamp(value, 0, 1), 3);
}

internal sealed class EmptyFumeDrawOperation(
    Rect bounds,
    FumeAudioEnergy energy,
    double opacity) : ICustomDrawOperation
{
    public Rect Bounds { get; } = bounds;

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature == null)
            return;
        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1.2f;
        paint.Color = new SKColor(214, 169, 31, (byte)(40 * Math.Clamp(opacity, 0, 1)));
        var pulse = 1 + energy.Mid * 0.12;
        canvas.DrawCircle(
            (float)(Bounds.Width * 0.5),
            (float)(Bounds.Height * 0.5),
            (float)(Math.Min(Bounds.Width, Bounds.Height) * 0.11 * pulse),
            paint);
    }

    public bool HitTest(Point p) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose()
    {
    }
}
