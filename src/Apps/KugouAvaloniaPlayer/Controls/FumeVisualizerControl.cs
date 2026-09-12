using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using ZLinq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaSilkEffects;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

// Folia's Fume visualizer expressed as an immutable article layout plus a moving camera.
public sealed class FumeVisualizerControl : SilkEffectControl
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

    public static readonly StyledProperty<bool> ParallaxEnabledProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, bool>(
            nameof(ParallaxEnabled),
            false);

    public static readonly StyledProperty<double> ParallaxMaxTiltProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, double>(
            nameof(ParallaxMaxTilt),
            14);

    public static readonly StyledProperty<double> ParallaxResponseProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, double>(
            nameof(ParallaxResponse),
            7.5);

    public static readonly StyledProperty<double> ParallaxOriginXProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, double>(
            nameof(ParallaxOriginX),
            0.5);

    public static readonly StyledProperty<double> ParallaxOriginYProperty =
        AvaloniaProperty.Register<FumeVisualizerControl, double>(
            nameof(ParallaxOriginY),
            0.5);

    private readonly FumeEffectScene _fumeScene = new();

    public FumeVisualizerControl()
    {
        RenderMode = EffectRenderMode.OnDemand;
        Scene = _fumeScene;
    }

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
    private MouseParallax3D? _parallax;

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

        ParallaxEnabledProperty.Changed.AddClassHandler<FumeVisualizerControl>(
            OnParallaxPropertyChanged);
        ParallaxMaxTiltProperty.Changed.AddClassHandler<FumeVisualizerControl>(
            OnParallaxPropertyChanged);
        ParallaxResponseProperty.Changed.AddClassHandler<FumeVisualizerControl>(
            OnParallaxPropertyChanged);
        ParallaxOriginXProperty.Changed.AddClassHandler<FumeVisualizerControl>(
            OnParallaxPropertyChanged);
        ParallaxOriginYProperty.Changed.AddClassHandler<FumeVisualizerControl>(
            OnParallaxPropertyChanged);
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

    public bool ParallaxEnabled
    {
        get => GetValue(ParallaxEnabledProperty);
        set => SetValue(ParallaxEnabledProperty, value);
    }

    public double ParallaxMaxTilt
    {
        get => GetValue(ParallaxMaxTiltProperty);
        set => SetValue(ParallaxMaxTiltProperty, value);
    }

    public double ParallaxResponse
    {
        get => GetValue(ParallaxResponseProperty);
        set => SetValue(ParallaxResponseProperty, value);
    }

    public double ParallaxOriginX
    {
        get => GetValue(ParallaxOriginXProperty);
        set => SetValue(ParallaxOriginXProperty, value);
    }

    public double ParallaxOriginY
    {
        get => GetValue(ParallaxOriginYProperty);
        set => SetValue(ParallaxOriginYProperty, value);
    }

    private static void OnParallaxPropertyChanged(
        FumeVisualizerControl control,
        AvaloniaPropertyChangedEventArgs e)
    {
        control.ApplyParallax();
    }

    private void ApplyParallax()
    {
        _parallax ??= new MouseParallax3D(this);
        _parallax.Enabled = ParallaxEnabled;
        _parallax.MaxTilt = ParallaxMaxTilt;
        _parallax.Response = ParallaxResponse;
        _parallax.Origin = new RelativePoint(ParallaxOriginX, ParallaxOriginY, RelativeUnit.Relative);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachPlayer(Player);
        MarkLayoutDirty(false);
        _parallax ??= new MouseParallax3D(this);
        _parallax.Attach();
        ApplyParallax();
        RequestNextFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        AttachPlayer(null);
        _fumeScene.Clear();
        _frameQueued = false;
        _hasFrameTimestamp = false;
        _parallax?.Detach();
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

        if (change.Property == BackgroundObjectOpacityProperty ||
            change.Property == TextHoldRatioProperty || change.Property == GlowIntensityProperty ||
            change.Property == CameraSpeedProperty || change.Property == CameraTrackingModeProperty)
        {
            _settleFrames = Math.Max(_settleFrames, 45);
            PublishFrame();
            RequestNextFrame();
        }
        if (change.Property == IsActiveProperty && change.NewValue is false)
        {
            _fumeScene.Clear();
            RenderOnce();
        }
        if (change.Property == IsActiveProperty && change.NewValue is true)
        {
            _hasFrameTimestamp = false;
            _settleFrames = 90;
            RequestNextFrame();
        }
    }

    private void PublishFrame()
    {
        if (Bounds.Width <= 1 || Bounds.Height <= 1 || Player == null || !IsActive)
        {
            _fumeScene.Clear();
            RenderOnce();
            return;
        }

        EnsureArticle();
        var article = _article;
        if (article == null)
        {
            _fumeScene.Clear();
            RenderOnce();
            return;
        }

        var player = Player;
        var seconds = player.CurrentPositionSeconds;
        var overview = ShouldShowOverview(article, seconds);
        if (!_hasFrameTimestamp)
            SnapCameraIfUninitialized(
                ResolveCameraTarget(article, player.CurrentLyricIndex, seconds, overview), article);
        _fumeScene.Publish(new Rect(Bounds.Size), new FumeFrame(
            article, _backgroundShapes, seconds, player.CurrentLyricIndex,
            _lastFrameTimestamp.TotalSeconds, _cameraX, _cameraY, _cameraScale,
            ResolveEnergy(player), Math.Clamp(BackgroundObjectOpacity, 0, 1),
            Math.Clamp(TextHoldRatio, 0, 1), Math.Clamp(GlowIntensity, 0, 1.8),
            LyricFontFamily.ToString(), overview));
        RenderOnce();
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
        _backgroundShapes = FumeBackgroundScene.Build(_article, viewport.Width, viewport.Height);
        ResetCamera();
    }

    private void MarkLayoutDirty(bool debounce)
    {
        _layoutDirty = true;
        _layoutRebuildAt = debounce
            ? DateTimeOffset.UtcNow + LayoutRebuildDelay
            : DateTimeOffset.MinValue;
        _settleFrames = Math.Max(_settleFrames, 12);
        PublishFrame();
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
            PublishFrame();
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
        PublishFrame();
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
        PublishFrame();
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
        var entryPoint = ResolveEntryFocusPoint(target);
        if (_cameraSourceIndex != target.SourceIndex)
        {
            _cameraSourceIndex = target.SourceIndex;
            _retargetElapsed = 0;
            _retargetFromX = _cameraX;
            _retargetFromY = _cameraY;
            _retargetFromScale = _cameraScale;
            var deltaX = entryPoint.X - _cameraX;
            var deltaY = entryPoint.Y - _cameraY;
            var screenDistance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY) *
                                 Math.Max(_cameraScale, target.Scale);
            var minimumSide = Math.Max(Math.Min(Bounds.Width, Bounds.Height), 1);
            _useOverviewBridge = target.SourceIndex >= 0 && screenDistance >= minimumSide * 2.75;
            _retargetDuration = _useOverviewBridge
                ? Math.Clamp((0.3 + screenDistance / minimumSide / 18) / speed, 0.22, 0.9)
                : Math.Clamp(0.1 / speed, 0.03, 0.3);

            var overview = _article == null ? target : ResolveOverviewTarget(_article);
            _bridgeX = Mix((_cameraX + entryPoint.X) * 0.5, overview.X, 0.32);
            _bridgeY = Mix((_cameraY + entryPoint.Y) * 0.5, overview.Y, 0.32);
            _bridgeScale = Math.Clamp(
                Math.Max(overview.Scale * 1.65, Math.Max(_cameraScale, target.Scale) * 0.48),
                CameraScaleMin,
                Math.Max(_cameraScale, target.Scale));
        }

        _retargetElapsed += deltaSeconds;
        var phase = Math.Clamp(_retargetElapsed / Math.Max(_retargetDuration, 0.001), 0, 1);
        var entryBias = Math.Pow(1 - EaseOutCubic(phase), 0.58);
        var targetX = Mix(target.X, entryPoint.X, entryBias);
        var targetY = Mix(target.Y, entryPoint.Y, entryBias);
        if (_useOverviewBridge && phase < 1)
        {
            var eased = EaseOutCubic(phase);
            var x = Quadratic(_retargetFromX, _bridgeX, targetX, eased);
            var y = Quadratic(_retargetFromY, _bridgeY, targetY, eased);
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
        _cameraVelocityX += ((targetX - _cameraX) * spring - _cameraVelocityX * damping) * deltaSeconds;
        _cameraVelocityY += ((targetY - _cameraY) * spring - _cameraVelocityY * damping) * deltaSeconds;
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

    private Point ResolveEntryFocusPoint(CameraTarget target)
    {
        if (_article == null || target.SourceIndex < 0 ||
            !_article.BlocksBySourceIndex.TryGetValue(target.SourceIndex, out var block) ||
            block.RenderLines.Count == 0)
            return new Point(target.X, target.Y);

        var firstLine = block.RenderLines[0];
        return new Point(block.X, block.Y + firstLine.Top + block.LineHeight * 0.5);
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

        Point PointOnLine(int lineIndex, double lineOffset)
        {
            var line = block.RenderLines[lineIndex];
            var clampedOffset = Math.Clamp(lineOffset, line.Start, line.End);
            var baseOffset = Math.Clamp((int)Math.Floor(clampedOffset), line.Start, line.End);
            var fraction = clampedOffset - Math.Floor(clampedOffset);
            var x = block.X + block.GlyphOffsets[baseOffset] - block.GlyphOffsets[line.Start];
            if (baseOffset < block.Graphemes.Count)
                x += (block.GlyphOffsets[baseOffset + 1] - block.GlyphOffsets[baseOffset]) * fraction;
            return new Point(
                Math.Clamp(x, block.X, block.X + line.Width),
                block.Y + lineIndex * block.LineHeight + block.LineHeight * 0.5);
        }

        var point = PointOnLine(targetLineIndex, offset);
        const double blendWindow = 0.7;
        var currentLine = block.RenderLines[targetLineIndex];
        if (targetLineIndex > 0 && offset < currentLine.Start + blendWindow)
        {
            var previousLine = block.RenderLines[targetLineIndex - 1];
            var blend = EaseInOutCubic(Math.Clamp(
                1 - (offset - previousLine.End) / blendWindow,
                0,
                1));
            var previous = PointOnLine(targetLineIndex - 1, previousLine.End);
            point = new Point(Mix(point.X, previous.X, blend), Mix(point.Y, previous.Y, blend));
        }
        else if (targetLineIndex < block.RenderLines.Count - 1 &&
                 offset > currentLine.End - blendWindow)
        {
            var nextLine = block.RenderLines[targetLineIndex + 1];
            var blend = EaseInOutCubic(Math.Clamp(
                (offset - (currentLine.End - blendWindow)) / blendWindow,
                0,
                1));
            var next = PointOnLine(targetLineIndex + 1, nextLine.Start);
            point = new Point(Mix(point.X, next.X, blend), Mix(point.Y, next.Y, blend));
        }

        return point;
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

    private static double Mix(double from, double to, double amount) =>
        from + (to - from) * amount;

    private static double EaseOutCubic(double value) =>
        1 - Math.Pow(1 - Math.Clamp(value, 0, 1), 3);

    private static double EaseInOutCubic(double value)
    {
        var normalized = Math.Clamp(value, 0, 1);
        return normalized < 0.5
            ? 4 * normalized * normalized * normalized
            : 1 - Math.Pow(-2 * normalized + 2, 3) / 2;
    }

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
