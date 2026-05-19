using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace AvaloniaLyrics;

public class LyricView : ItemsControl
{
    private static readonly TimeSpan MaxPositionClockDrift = TimeSpan.FromMilliseconds(240);

    private const int StaggerRange = 10;
    private const int StaggerStepMs = 20;
    private const int EntranceStepMs = 12;
    private const double EntranceRiseOffset = 48;
    private const double BaseSpringStiffness = 0.075;
    private const double BaseSpringDamping = 0.76;
    private const double BaseScrollDurationMs = 256;
    private const double ManualOffsetReturnStiffness = 0.052;
    private const double ManualOffsetReturnDamping = 0.78;
    private const double OpacityResponse = 9.0;
    private const double SettleTopThreshold = 0.22;
    private const double SettleVelocityThreshold = 0.12;
    private const double SettleManualOffsetThreshold = 0.35;
    private const double SettleManualVelocityThreshold = 0.2;
    private const double SettleOpacityThreshold = 0.008;

    private const double DefaultEstimatedLineHeight = 72d;
    private const double VisualTopUpdateThreshold = 0.05d;
    private const double VisualOpacityUpdateThreshold = 0.002d;
    private const double VisualScaleUpdateThreshold = 0.0005d;

    public static readonly StyledProperty<IEnumerable<LyricLine>?> LinesProperty =
        AvaloniaProperty.Register<LyricView, IEnumerable<LyricLine>?>(nameof(Lines));

    public static readonly StyledProperty<TimeSpan> PositionProperty =
        AvaloniaProperty.Register<LyricView, TimeSpan>(nameof(Position));

    public static readonly StyledProperty<bool> IsPositionClockRunningProperty =
        AvaloniaProperty.Register<LyricView, bool>(nameof(IsPositionClockRunning));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LyricView, bool>(nameof(IsActive), true);

    public static readonly StyledProperty<LyricViewPreset> PresetProperty =
        AvaloniaProperty.Register<LyricView, LyricViewPreset>(nameof(Preset));

    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<LyricView, double>(nameof(LineSpacing), 14d);

    public static readonly StyledProperty<TimeSpan> ScrollDurationProperty =
        AvaloniaProperty.Register<LyricView, TimeSpan>(nameof(ScrollDuration), TimeSpan.FromMilliseconds(420));

    public static readonly StyledProperty<double> ActiveAnchorRatioProperty =
        AvaloniaProperty.Register<LyricView, double>(nameof(ActiveAnchorRatio), 0.5d);

    public static readonly StyledProperty<double> EdgeFadeRatioProperty =
        AvaloniaProperty.Register<LyricView, double>(nameof(EdgeFadeRatio), 0.15d);

    public static readonly StyledProperty<bool> EnableAnimationProperty =
        AvaloniaProperty.Register<LyricView, bool>(nameof(EnableAnimation));

    public static readonly StyledProperty<bool> EnableStaggerProperty =
        AvaloniaProperty.Register<LyricView, bool>(nameof(EnableStagger));

    public static readonly StyledProperty<bool> EnableScaleProperty =
        AvaloniaProperty.Register<LyricView, bool>(nameof(EnableScale));

    public static readonly StyledProperty<double> InactiveScaleProperty =
        AvaloniaProperty.Register<LyricView, double>(nameof(InactiveScale), 0.97d);

    public static readonly StyledProperty<bool> ShowTranslationProperty =
        AvaloniaProperty.Register<LyricView, bool>(nameof(ShowTranslation), true);

    public static readonly StyledProperty<bool> ShowRomanizationProperty =
        AvaloniaProperty.Register<LyricView, bool>(nameof(ShowRomanization), true);

    public static readonly StyledProperty<LyricWordRenderMode> WordRenderModeProperty =
        AvaloniaProperty.Register<LyricView, LyricWordRenderMode>(nameof(WordRenderMode), LyricWordRenderMode.Clip);

    public static readonly StyledProperty<Thickness> ItemMarginProperty =
        AvaloniaProperty.Register<LyricView, Thickness>(nameof(ItemMargin));

    public static readonly StyledProperty<double> SecondaryTopSpacingProperty =
        AvaloniaProperty.Register<LyricView, double>(nameof(SecondaryTopSpacing));

    public static readonly StyledProperty<KaraokeClipMode> ClipModeProperty =
        AvaloniaProperty.Register<LyricView, KaraokeClipMode>(nameof(ClipMode), KaraokeClipMode.ByTextWidth);

    public new static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextBlock.FontFamilyProperty.AddOwner<LyricView>();

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        TextBlock.TextAlignmentProperty.AddOwner<LyricView>();

    public static readonly StyledProperty<double> PrimaryFontSizeProperty =
        AvaloniaProperty.Register<LyricView, double>(nameof(PrimaryFontSize), 26d);

    public static readonly StyledProperty<double> TranslationFontSizeProperty =
        AvaloniaProperty.Register<LyricView, double>(nameof(TranslationFontSize), 16d);

    public static readonly StyledProperty<double> RomanizationFontSizeProperty =
        AvaloniaProperty.Register<LyricView, double>(nameof(RomanizationFontSize), 16d);

    public static readonly StyledProperty<IBrush> PrimaryForegroundProperty =
        AvaloniaProperty.Register<LyricView, IBrush>(nameof(PrimaryForeground), Brushes.White);

    public static readonly StyledProperty<IBrush> PrimaryPlayedForegroundProperty =
        AvaloniaProperty.Register<LyricView, IBrush>(nameof(PrimaryPlayedForeground), Brushes.White);

    public static readonly StyledProperty<IBrush> TranslationForegroundProperty =
        AvaloniaProperty.Register<LyricView, IBrush>(nameof(TranslationForeground),
            new SolidColorBrush(Color.Parse("#CCFFFFFF")));

    public static readonly StyledProperty<IBrush> TranslationPlayedForegroundProperty =
        AvaloniaProperty.Register<LyricView, IBrush>(nameof(TranslationPlayedForeground), Brushes.White);

    public static readonly StyledProperty<IBrush> RomanizationForegroundProperty =
        AvaloniaProperty.Register<LyricView, IBrush>(nameof(RomanizationForeground),
            new SolidColorBrush(Color.Parse("#CCFFFFFF")));

    public static readonly DirectProperty<LyricView, LyricLine?> ActiveLineProperty =
        AvaloniaProperty.RegisterDirect<LyricView, LyricLine?>(nameof(ActiveLine), o => o.ActiveLine);

    public static readonly DirectProperty<LyricView, int> ActiveIndexProperty =
        AvaloniaProperty.RegisterDirect<LyricView, int>(nameof(ActiveIndex), o => o.ActiveIndex);

    public static readonly DirectProperty<LyricView, TimeSpan> RenderPositionProperty =
        AvaloniaProperty.RegisterDirect<LyricView, TimeSpan>(nameof(RenderPosition), o => o.RenderPosition);

    private readonly HashSet<Control> _activeContainers = new();

    private readonly Dictionary<int, double> _knownHeights = new();
    private readonly Dictionary<Control, SpringState> _springStates = new();
    private readonly DispatcherTimer _userScrollResetTimer;
    private int _activeIndex = -1;
    private LyricLine? _activeLine;
    private bool _animationFrameQueued;
    private INotifyCollectionChanged? _collectionChangedSource;
    private bool _deferredActiveItemUpdate;
    private bool _hasLastFrameTimestamp;
    private bool _isApplyingPreset;
    private bool _isFirstLayoutPass = true;
    private bool _isManualOffsetReturning;
    private bool _isUserScrolling;
    private TimeSpan _lastFrameTimestamp;
    private bool _layoutUpdateQueued;
    private double[] _lineCenters = [];
    private double[] _lineHeights = [];
    private IReadOnlyList<LyricLine> _linesSnapshot = [];
    private int? _lockedActiveIndex;
    private double _manualOffset;
    private double _manualOffsetTarget;
    private double _manualOffsetVelocity;
    private TimeSpan _positionAnchor;
    private long _positionAnchorTimestamp;
    private bool _positionFrameQueued;
    private TimeSpan _renderPosition;

    public LyricView()
    {
        _userScrollResetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _userScrollResetTimer.Tick += OnUserScrollTimeout;
        LayoutUpdated += OnLayoutUpdated;
        ItemTemplate = CreateDefaultItemTemplate();
        _positionAnchor = Position;
        _positionAnchorTimestamp = Stopwatch.GetTimestamp();
        _renderPosition = Position;
    }

    protected override Type StyleKeyOverride => typeof(LyricView);

    public IEnumerable<LyricLine>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public TimeSpan Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public bool IsPositionClockRunning
    {
        get => GetValue(IsPositionClockRunningProperty);
        set => SetValue(IsPositionClockRunningProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public LyricViewPreset Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    public TimeSpan ScrollDuration
    {
        get => GetValue(ScrollDurationProperty);
        set => SetValue(ScrollDurationProperty, value);
    }

    public double ActiveAnchorRatio
    {
        get => GetValue(ActiveAnchorRatioProperty);
        set => SetValue(ActiveAnchorRatioProperty, value);
    }

    public double EdgeFadeRatio
    {
        get => GetValue(EdgeFadeRatioProperty);
        set => SetValue(EdgeFadeRatioProperty, value);
    }

    public bool EnableScale
    {
        get => GetValue(EnableScaleProperty);
        set => SetValue(EnableScaleProperty, value);
    }

    public bool EnableAnimation
    {
        get => GetValue(EnableAnimationProperty);
        set => SetValue(EnableAnimationProperty, value);
    }

    public bool EnableStagger
    {
        get => GetValue(EnableStaggerProperty);
        set => SetValue(EnableStaggerProperty, value);
    }

    public double InactiveScale
    {
        get => GetValue(InactiveScaleProperty);
        set => SetValue(InactiveScaleProperty, value);
    }

    public bool ShowTranslation
    {
        get => GetValue(ShowTranslationProperty);
        set => SetValue(ShowTranslationProperty, value);
    }

    public bool ShowRomanization
    {
        get => GetValue(ShowRomanizationProperty);
        set => SetValue(ShowRomanizationProperty, value);
    }

    public Thickness ItemMargin
    {
        get => GetValue(ItemMarginProperty);
        set => SetValue(ItemMarginProperty, value);
    }

    public double SecondaryTopSpacing
    {
        get => GetValue(SecondaryTopSpacingProperty);
        set => SetValue(SecondaryTopSpacingProperty, value);
    }

    public LyricWordRenderMode WordRenderMode
    {
        get => GetValue(WordRenderModeProperty);
        set => SetValue(WordRenderModeProperty, value);
    }

    public KaraokeClipMode ClipMode
    {
        get => GetValue(ClipModeProperty);
        set => SetValue(ClipModeProperty, value);
    }

    public new FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public double PrimaryFontSize
    {
        get => GetValue(PrimaryFontSizeProperty);
        set => SetValue(PrimaryFontSizeProperty, value);
    }

    public double TranslationFontSize
    {
        get => GetValue(TranslationFontSizeProperty);
        set => SetValue(TranslationFontSizeProperty, value);
    }

    public double RomanizationFontSize
    {
        get => GetValue(RomanizationFontSizeProperty);
        set => SetValue(RomanizationFontSizeProperty, value);
    }

    public IBrush PrimaryForeground
    {
        get => GetValue(PrimaryForegroundProperty);
        set => SetValue(PrimaryForegroundProperty, value);
    }

    public IBrush PrimaryPlayedForeground
    {
        get => GetValue(PrimaryPlayedForegroundProperty);
        set => SetValue(PrimaryPlayedForegroundProperty, value);
    }

    public IBrush TranslationForeground
    {
        get => GetValue(TranslationForegroundProperty);
        set => SetValue(TranslationForegroundProperty, value);
    }

    public IBrush TranslationPlayedForeground
    {
        get => GetValue(TranslationPlayedForegroundProperty);
        set => SetValue(TranslationPlayedForegroundProperty, value);
    }

    public IBrush RomanizationForeground
    {
        get => GetValue(RomanizationForegroundProperty);
        set => SetValue(RomanizationForegroundProperty, value);
    }

    public LyricLine? ActiveLine => _activeLine;

    public int ActiveIndex => _activeIndex;

    public TimeSpan RenderPosition => _renderPosition;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopPositionClock(syncToPosition: false);
        _animationFrameQueued = false;
        _hasLastFrameTimestamp = false;
        _userScrollResetTimer.Stop();
        UnhookCollectionChanged();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PresetProperty)
        {
            ApplyPreset((LyricViewPreset)change.NewValue!);
            return;
        }

        if (change.Property == LinesProperty)
        {
            ItemsSource = Lines;
            HookCollectionChanged(change.NewValue);
            RefreshLinesSnapshot();
            ResetFirstLayoutState();
            SyncActiveLine();
            if (IsActive)
                QueueLayoutUpdate();
            return;
        }

        if (change.Property == PositionProperty)
        {
            if (!IsActive)
                return;

            SyncPositionAnchor();
            SetRenderPosition(Position);
            var activeLineChanged = SyncActiveLine();
            if (_isUserScrolling)
            {
                _deferredActiveItemUpdate = true;
                EnsurePositionClockRunning();
                return;
            }

            if (activeLineChanged)
                QueueLayoutUpdate();

            EnsurePositionClockRunning();
            return;
        }

        if (change.Property == IsActiveProperty)
        {
            if (IsActive)
            {
                SyncPositionAnchor();
                SetRenderPosition(Position);
                SyncActiveLine();
                QueueLayoutUpdate();
                EnsureAnimationFrameRunning();
                EnsurePositionClockRunning();
            }
            else
            {
                StopAnimationFrames();
                StopPositionClock(syncToPosition: true);
            }

            return;
        }

        if (change.Property == IsPositionClockRunningProperty)
        {
            if (!ShouldRunPositionClock())
                StopPositionClock(syncToPosition: true);
            else
                EnsurePositionClockRunning();

            return;
        }

        if (change.Property == BoundsProperty ||
            change.Property == LineSpacingProperty ||
            change.Property == ScrollDurationProperty ||
            change.Property == ActiveAnchorRatioProperty ||
            change.Property == EdgeFadeRatioProperty ||
            change.Property == EnableAnimationProperty ||
            change.Property == EnableStaggerProperty ||
            change.Property == EnableScaleProperty)
        {
            if (IsActive)
                QueueLayoutUpdate();
            return;
        }

        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                if (IsActive)
                    QueueLayoutUpdate();
                EnsureAnimationFrameRunning();
                EnsurePositionClockRunning();
            }
            else
            {
                StopAnimationFrames();
                StopPositionClock(syncToPosition: true);
            }
        }
    }

    private void ApplyPreset(LyricViewPreset preset)
    {
        if (_isApplyingPreset)
            return;

        _isApplyingPreset = true;
        try
        {
            switch (preset)
            {
                case LyricViewPreset.AppleMusicClassic:
                    ActiveAnchorRatio = 0.34d;
                    ScrollDuration = TimeSpan.FromMilliseconds(370);
                    EnableAnimation = true;
                    EnableStagger = true;
                    EnableScale = true;
                    InactiveScale = 0.97d;
                    LineSpacing = 22d;
                    ItemMargin = new Thickness(0d, 15d);
                    SecondaryTopSpacing = 5d;
                    EdgeFadeRatio = 0.15d;
                    ClipMode = KaraokeClipMode.ByTextWidth;
                    break;

                case LyricViewPreset.DesktopClassic:
                    ActiveAnchorRatio = 0.5d;
                    ScrollDuration = TimeSpan.FromMilliseconds(420);
                    EnableAnimation = false;
                    EnableStagger = false;
                    EnableScale = false;
                    InactiveScale = 0.97d;
                    LineSpacing = 14d;
                    ItemMargin = default;
                    SecondaryTopSpacing = 0d;
                    EdgeFadeRatio = 0.15d;
                    ClipMode = KaraokeClipMode.ByTextWidth;
                    break;

                default:
                    ActiveAnchorRatio = 0.5d;
                    ScrollDuration = TimeSpan.FromMilliseconds(420);
                    EnableAnimation = false;
                    EnableStagger = false;
                    EnableScale = false;
                    InactiveScale = 0.97d;
                    LineSpacing = 14d;
                    ItemMargin = default;
                    SecondaryTopSpacing = 0d;
                    EdgeFadeRatio = 0.15d;
                    ClipMode = KaraokeClipMode.ByTextWidth;
                    break;
            }
        }
        finally
        {
            _isApplyingPreset = false;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (ItemCount == 0)
            return;

        if (!_isUserScrolling)
            _lockedActiveIndex = ActiveIndex;

        _isUserScrolling = true;
        _isManualOffsetReturning = false;

        var wheelOffset = e.Delta.Y * 80d;
        _manualOffset += wheelOffset;
        _manualOffsetTarget = _manualOffset;
        _manualOffsetVelocity = wheelOffset * 0.35d;

        _userScrollResetTimer.Stop();
        _userScrollResetTimer.Start();

        QueueLayoutUpdate();
        e.Handled = true;
    }

    private FuncDataTemplate<LyricLine> CreateDefaultItemTemplate()
    {
        return new FuncDataTemplate<LyricLine>((_, _) =>
        {
            var control = new LyricLineControl();

            control.Bind(
                LyricLineControl.LineProperty,
                CompiledBinding.Create<LyricLine, LyricLine>(x => x));

            control.Bind(
                LyricLineControl.ActiveLineProperty,
                CompiledBinding.Create<LyricView, LyricLine?>(x => x.ActiveLine, this));

            control.Bind(
                LyricLineControl.PositionProperty,
                CompiledBinding.Create<LyricView, TimeSpan>(x => x.RenderPosition, this));

            control.Bind(
                LyricLineControl.ShowTranslationProperty,
                CompiledBinding.Create<LyricView, bool>(x => x.ShowTranslation, this));

            control.Bind(
                LyricLineControl.ShowRomanizationProperty,
                CompiledBinding.Create<LyricView, bool>(x => x.ShowRomanization, this));

            control.Bind(
                LyricLineControl.WordRenderModeProperty,
                CompiledBinding.Create<LyricView, LyricWordRenderMode>(x => x.WordRenderMode, this));

            control.Bind(
                LyricLineControl.ClipModeProperty,
                CompiledBinding.Create<LyricView, KaraokeClipMode>(x => x.ClipMode, this));

            control.Bind(
                MarginProperty,
                CompiledBinding.Create<LyricView, Thickness>(x => x.ItemMargin, this));

            control.Bind(
                LyricLineControl.FontFamilyProperty,
                CompiledBinding.Create<LyricView, FontFamily>(x => x.FontFamily, this));

            control.Bind(
                LyricLineControl.TextAlignmentProperty,
                CompiledBinding.Create<LyricView, TextAlignment>(x => x.TextAlignment, this));

            control.Bind(
                LyricLineControl.SecondaryTopSpacingProperty,
                CompiledBinding.Create<LyricView, double>(x => x.SecondaryTopSpacing, this));

            control.Bind(
                LyricLineControl.PrimaryFontSizeProperty,
                CompiledBinding.Create<LyricView, double>(x => x.PrimaryFontSize, this));

            control.Bind(
                LyricLineControl.TranslationFontSizeProperty,
                CompiledBinding.Create<LyricView, double>(x => x.TranslationFontSize, this));

            control.Bind(
                LyricLineControl.RomanizationFontSizeProperty,
                CompiledBinding.Create<LyricView, double>(x => x.RomanizationFontSize, this));

            control.Bind(
                LyricLineControl.PrimaryForegroundProperty,
                CompiledBinding.Create<LyricView, IBrush?>(x => x.PrimaryForeground, this));

            control.Bind(
                LyricLineControl.PrimaryPlayedForegroundProperty,
                CompiledBinding.Create<LyricView, IBrush?>(x => x.PrimaryPlayedForeground, this));

            control.Bind(
                LyricLineControl.TranslationForegroundProperty,
                CompiledBinding.Create<LyricView, IBrush?>(x => x.TranslationForeground, this));

            control.Bind(
                LyricLineControl.TranslationPlayedForegroundProperty,
                CompiledBinding.Create<LyricView, IBrush?>(x => x.TranslationPlayedForeground, this));

            control.Bind(
                LyricLineControl.RomanizationForegroundProperty,
                CompiledBinding.Create<LyricView, IBrush?>(x => x.RomanizationForeground, this));

            return control;
        });
    }

    private void OnUserScrollTimeout(object? sender, EventArgs e)
    {
        _userScrollResetTimer.Stop();
        _isUserScrolling = false;
        _lockedActiveIndex = null;
        _manualOffsetTarget = 0d;

        if (EnableAnimation)
        {
            _isManualOffsetReturning = Math.Abs(_manualOffset) > SettleManualOffsetThreshold;
        }
        else
        {
            _manualOffset = 0d;
            _manualOffsetVelocity = 0d;
            _isManualOffsetReturning = false;
        }

        if (_deferredActiveItemUpdate)
            _deferredActiveItemUpdate = false;

        if (IsActive)
            QueueLayoutUpdate();
    }

    private void HookCollectionChanged(object? itemsSource)
    {
        UnhookCollectionChanged();

        if (itemsSource is INotifyCollectionChanged changed)
        {
            _collectionChangedSource = changed;
            _collectionChangedSource.CollectionChanged += OnItemsSourceCollectionChanged;
        }
    }

    private void UnhookCollectionChanged()
    {
        if (_collectionChangedSource == null)
            return;

        _collectionChangedSource.CollectionChanged -= OnItemsSourceCollectionChanged;
        _collectionChangedSource = null;
    }

    private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshLinesSnapshot();
        SyncActiveLine();
        ResetFirstLayoutState();
        if (IsActive)
            QueueLayoutUpdate();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_isUserScrolling)
            return;

        if (RefreshMeasuredHeights())
            QueueLayoutUpdate();
    }

    private void QueueLayoutUpdate()
    {
        if (_layoutUpdateQueued)
            return;

        if (!IsActive)
            return;

        _layoutUpdateQueued = true;
        Dispatcher.Post(() =>
        {
            _layoutUpdateQueued = false;
            ApplyMeasuredLayout();
        }, DispatcherPriority.Render);
    }

    private void ApplyMeasuredLayout()
    {
        if (ItemCount == 0 || Bounds.Height <= 0 || Bounds.Width <= 0)
            return;

        if (!ShouldRunAnimationFrames())
            return;

        var activeIndex = _isUserScrolling
            ? _lockedActiveIndex ?? ActiveIndex
            : ActiveIndex;

        if (activeIndex < 0 || activeIndex >= ItemCount)
            activeIndex = 0;

        EnsureLayoutBuffers(ItemCount);

        var naturalTop = 0d;
        for (var i = 0; i < ItemCount; i++)
        {
            if (ContainerFromIndex(i) is not Control container)
                continue;

            var height = container.Bounds.Height;
            if (height <= 0d)
                height = container.DesiredSize.Height;
            if (height <= 0d)
                height = _knownHeights.GetValueOrDefault(i, DefaultEstimatedLineHeight);
            else
                _knownHeights[i] = height;

            _lineHeights[i] = height;
            _lineCenters[i] = naturalTop + height / 2d;
            naturalTop += height + LineSpacing;
        }

        var centerY = Bounds.Height * Math.Clamp(ActiveAnchorRatio, 0d, 1d) + _manualOffset;
        var activeNaturalCenter = _lineCenters[activeIndex];
        _activeContainers.Clear();

        for (var i = 0; i < ItemCount; i++)
        {
            if (ContainerFromIndex(i) is not Control container)
                continue;

            _activeContainers.Add(container);

            var height = _lineHeights[i];
            var targetTop = centerY + (_lineCenters[i] - activeNaturalCenter) - height / 2d;
            Canvas.SetLeft(container, 0d);
            Canvas.SetTop(container, 0d);
            container.Width = Bounds.Width;

            var distance = Math.Abs(i - activeIndex);
            double targetOpacity;
            if (distance == 0)
                targetOpacity = 1d;
            else if (distance == 1)
                targetOpacity = 0.88d;
            else if (distance == 2)
                targetOpacity = 0.72d;
            else
                targetOpacity = Math.Clamp(0.58d - (distance - 3) * 0.10d, 0.16d, 1d);

            targetOpacity *= CalculateEdgeFadeFactor(targetTop, height);

            var targetScale = 1d;
            if (EnableScale && distance > 0)
                targetScale = InactiveScale;

            UpdateSpringState(container, targetTop, targetOpacity, targetScale, i, activeIndex);
        }

        TrimStaleStates(_activeContainers);
        _isFirstLayoutPass = false;
        EnsureAnimationFrameRunning();
    }

    private void EnsureLayoutBuffers(int itemCount)
    {
        if (_lineHeights.Length >= itemCount && _lineCenters.Length >= itemCount)
            return;

        var capacity = Math.Max(itemCount, Math.Max(_lineHeights.Length, 8) * 2);
        _lineHeights = new double[capacity];
        _lineCenters = new double[capacity];
    }

    private bool SyncActiveLine()
    {
        if (_linesSnapshot.Count == 0)
        {
            var changed = _activeLine != null || _activeIndex != -1;
            SetAndRaise(ActiveLineProperty, ref _activeLine, null);
            SetAndRaise(ActiveIndexProperty, ref _activeIndex, -1);
            return changed;
        }

        var position = RenderPosition;
        var resultIndex = 0;
        var left = 0;
        var right = _linesSnapshot.Count - 1;

        if (position < _linesSnapshot[0].Start)
            resultIndex = 0;
        else if (position >= _linesSnapshot[^1].Start)
            resultIndex = _linesSnapshot.Count - 1;
        else
            while (left <= right)
            {
                var mid = left + (right - left) / 2;
                if (_linesSnapshot[mid].Start <= position)
                {
                    resultIndex = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

        var nextActiveLine = _linesSnapshot[resultIndex];
        var changedActiveLine = !ReferenceEquals(_activeLine, nextActiveLine) || _activeIndex != resultIndex;
        SetAndRaise(ActiveLineProperty, ref _activeLine, nextActiveLine);
        SetAndRaise(ActiveIndexProperty, ref _activeIndex, resultIndex);
        return changedActiveLine;
    }

    private void RefreshLinesSnapshot()
    {
        _linesSnapshot = Lines switch
        {
            null => [],
            IReadOnlyList<LyricLine> readOnlyList => readOnlyList,
            IList<LyricLine> list => list.ToList(),
            var enumerable => enumerable.ToList()
        };
    }

    private void UpdateSpringState(Control container, double targetTop, double targetOpacity, double targetScale,
        int index, int activeIndex)
    {
        var isEntrance = EnableAnimation && _isFirstLayoutPass && !_isUserScrolling;
        var topDelay = isEntrance
            ? GetEntranceDelay(index, activeIndex)
            : _isManualOffsetReturning
                ? TimeSpan.Zero
                : EnableStagger
                    ? GetTopTransitionDelay(index, activeIndex + 1)
                    : TimeSpan.Zero;

        if (!_springStates.TryGetValue(container, out var state))
        {
            state = new SpringState();
            _springStates[container] = state;
        }

        if (_isUserScrolling || !EnableAnimation)
        {
            state.CurrentTop = targetTop;
            state.TargetTop = targetTop;
            state.Velocity = 0d;
            state.CurrentOpacity = targetOpacity;
            state.TargetOpacity = targetOpacity;
            state.CurrentScale = targetScale;
            state.TargetScale = targetScale;
            state.ClearPendingTarget();
            state.IsInitialized = true;
            ApplyVisualState(container, state);
            return;
        }

        if (!state.IsInitialized)
        {
            state.CurrentTop = isEntrance
                ? targetTop + EntranceRiseOffset + Math.Abs(index - activeIndex) * 8d
                : targetTop;
            state.Velocity = 0d;
            state.CurrentOpacity = isEntrance ? 0d : targetOpacity;
            state.CurrentScale = isEntrance ? 0.985d : targetScale;
            state.IsInitialized = true;

            if (topDelay > TimeSpan.Zero)
            {
                state.TargetTop = state.CurrentTop;
                state.TargetOpacity = state.CurrentOpacity;
                state.TargetScale = state.CurrentScale;
                state.QueueTarget(targetTop, targetOpacity, targetScale, topDelay.TotalSeconds);
            }
            else
            {
                state.TargetTop = targetTop;
                state.TargetOpacity = targetOpacity;
                state.TargetScale = targetScale;
                state.ClearPendingTarget();
            }

            ApplyVisualState(container, state);
            return;
        }

        state.ScheduleTarget(targetTop, targetOpacity, targetScale, topDelay.TotalSeconds);
    }

    private double CalculateEdgeFadeFactor(double top, double height)
    {
        var fadeLength = Bounds.Height * Math.Clamp(EdgeFadeRatio, 0d, 0.45d);
        if (fadeLength <= 0d)
            return 1d;

        var center = top + height / 2d;
        var topFactor = SmoothStep(Math.Clamp(center / fadeLength, 0d, 1d));
        var bottomFactor = SmoothStep(Math.Clamp((Bounds.Height - center) / fadeLength, 0d, 1d));
        return Math.Min(topFactor, bottomFactor);
    }

    private static double SmoothStep(double value)
    {
        return value * value * (3d - 2d * value);
    }

    private static TimeSpan GetEntranceDelay(int index, int activeIndex)
    {
        var distance = Math.Abs(index - activeIndex);
        return TimeSpan.FromMilliseconds(Math.Min(220, distance * EntranceStepMs));
    }

    private static TimeSpan GetTopTransitionDelay(int index, int activeIndex)
    {
        var delta = index - activeIndex;
        if (Math.Abs(delta) > StaggerRange)
            return TimeSpan.Zero;

        var delayMs = (StaggerRange + delta) * StaggerStepMs;
        return TimeSpan.FromMilliseconds(Math.Max(0, delayMs));
    }

    private bool RefreshMeasuredHeights()
    {
        var changed = false;
        for (var i = 0; i < ItemCount; i++)
        {
            if (ContainerFromIndex(i) is not Control container)
                continue;

            var height = container.Bounds.Height;
            if (height <= 0d)
                continue;

            if (!_knownHeights.TryGetValue(i, out var knownHeight) || Math.Abs(knownHeight - height) > 0.5d)
            {
                _knownHeights[i] = height;
                changed = true;
            }
        }

        return changed;
    }

    private void EnsurePositionClockRunning()
    {
        if (_positionFrameQueued || !ShouldRunPositionClock())
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        _positionFrameQueued = true;
        topLevel.RequestAnimationFrame(OnPositionAnimationFrame);
    }

    private void StopPositionClock(bool syncToPosition)
    {
        _positionFrameQueued = false;
        if (!syncToPosition)
            return;

        SyncPositionAnchor();
        SetRenderPosition(Position);
        var activeLineChanged = SyncActiveLine();
        if (activeLineChanged && IsActive && IsVisible && !_isUserScrolling)
            QueueLayoutUpdate();
    }

    private bool ShouldRunPositionClock()
    {
        return IsPositionClockRunning &&
               IsActive &&
               IsVisible &&
               ItemCount > 0 &&
               Bounds.Height > 0d &&
               Bounds.Width > 0d &&
               TopLevel.GetTopLevel(this) != null;
    }

    private void SyncPositionAnchor()
    {
        _positionAnchor = Position;
        _positionAnchorTimestamp = Stopwatch.GetTimestamp();
    }

    private void SetRenderPosition(TimeSpan value)
    {
        SetAndRaise(RenderPositionProperty, ref _renderPosition, value);
    }

    private void OnPositionAnimationFrame(TimeSpan timestamp)
    {
        _positionFrameQueued = false;
        if (!ShouldRunPositionClock())
        {
            StopPositionClock(syncToPosition: true);
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_positionAnchorTimestamp);
        var cappedElapsed = elapsed > MaxPositionClockDrift ? MaxPositionClockDrift : elapsed;
        SetRenderPosition(_positionAnchor + cappedElapsed);

        var activeLineChanged = SyncActiveLine();
        if (activeLineChanged && !_isUserScrolling)
            QueueLayoutUpdate();
        else if (_isUserScrolling)
            _deferredActiveItemUpdate = true;

        EnsurePositionClockRunning();
    }

    private void EnsureAnimationFrameRunning()
    {
        if (!EnableAnimation)
            return;

        if (_isUserScrolling || (_springStates.Count == 0 && !_isManualOffsetReturning))
            return;

        if (_animationFrameQueued || !ShouldRunAnimationFrames())
            return;

        RequestNextAnimationFrame();
    }

    private void RequestNextAnimationFrame()
    {
        if (_animationFrameQueued)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        _animationFrameQueued = true;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void StopAnimationFrames()
    {
        _animationFrameQueued = false;
        _hasLastFrameTimestamp = false;
    }

    private bool ShouldRunAnimationFrames()
    {
        if (!EnableAnimation)
            return false;

        return IsActive &&
               IsVisible &&
               ItemCount > 0 &&
               Bounds.Height > 0d &&
               Bounds.Width > 0d &&
               TopLevel.GetTopLevel(this) != null;
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        _animationFrameQueued = false;
        if (!ShouldRunAnimationFrames())
        {
            _hasLastFrameTimestamp = false;
            return;
        }

        if (_springStates.Count == 0 && !_isManualOffsetReturning)
            return;

        var elapsed = _hasLastFrameTimestamp
            ? timestamp - _lastFrameTimestamp
            : TimeSpan.FromSeconds(1d / 60d);
        _hasLastFrameTimestamp = true;
        _lastFrameTimestamp = timestamp;
        var deltaSeconds = Math.Clamp(elapsed.TotalSeconds, 1d / 240d, 1d / 20d);
        var frameFactor = deltaSeconds * 60d;
        var durationFactor = Math.Clamp(BaseScrollDurationMs / Math.Max(120d, ScrollDuration.TotalMilliseconds), 0.55d,
            2.2d);
        var springStiffness = BaseSpringStiffness * durationFactor * durationFactor;
        var springDamping = Math.Pow(BaseSpringDamping, durationFactor);
        var opacityFactor = 1d - Math.Exp(-OpacityResponse * deltaSeconds);

        var hasActiveMotion = false;
        if (UpdateManualOffset(frameFactor))
            ApplyMeasuredLayout();

        foreach (var (container, state) in _springStates)
        {
            if (!state.IsInitialized)
                continue;

            state.UpdatePendingTarget(deltaSeconds);

            var targetDelta = state.TargetTop - state.CurrentTop;
            state.Velocity += targetDelta * springStiffness * frameFactor;
            state.Velocity *= Math.Pow(springDamping, frameFactor);
            state.CurrentTop += state.Velocity * frameFactor;

            state.CurrentOpacity += (state.TargetOpacity - state.CurrentOpacity) * opacityFactor;
            state.CurrentScale += (state.TargetScale - state.CurrentScale) * opacityFactor;

            if (Math.Abs(targetDelta) > SettleTopThreshold ||
                Math.Abs(state.Velocity) > SettleVelocityThreshold ||
                Math.Abs(state.TargetOpacity - state.CurrentOpacity) > SettleOpacityThreshold)
            {
                hasActiveMotion = true;
            }
            else
            {
                state.CurrentTop = state.TargetTop;
                state.Velocity = 0d;
            }

            if (Math.Abs(state.TargetOpacity - state.CurrentOpacity) > SettleOpacityThreshold)
                hasActiveMotion = true;
            else
                state.CurrentOpacity = state.TargetOpacity;

            if (Math.Abs(state.TargetScale - state.CurrentScale) > 0.001d)
                hasActiveMotion = true;
            else
                state.CurrentScale = state.TargetScale;

            ApplyVisualState(container, state);
        }

        if (!hasActiveMotion)
        {
            _hasLastFrameTimestamp = false;
            return;
        }

        RequestNextAnimationFrame();
    }

    private bool UpdateManualOffset(double frameFactor)
    {
        if (!_isManualOffsetReturning)
            return false;

        var displacement = _manualOffsetTarget - _manualOffset;
        _manualOffsetVelocity += displacement * ManualOffsetReturnStiffness * frameFactor;
        _manualOffsetVelocity *= Math.Pow(ManualOffsetReturnDamping, frameFactor);
        _manualOffset += _manualOffsetVelocity * frameFactor;

        if (Math.Abs(_manualOffsetTarget - _manualOffset) <= SettleManualOffsetThreshold &&
            Math.Abs(_manualOffsetVelocity) <= SettleManualVelocityThreshold)
        {
            _manualOffset = _manualOffsetTarget;
            _manualOffsetVelocity = 0d;
            _isManualOffsetReturning = false;
            return true;
        }

        return true;
    }

    private static void ApplyVisualState(Control container, SpringState state)
    {
        state.EnsureTransform(container);

        var topChanged = !state.HasAppliedVisualState ||
                         Math.Abs(state.LastAppliedTop - state.CurrentTop) > VisualTopUpdateThreshold;
        var scaleChanged = !state.HasAppliedVisualState ||
                           Math.Abs(state.LastAppliedScale - state.CurrentScale) > VisualScaleUpdateThreshold;
        var opacityChanged = !state.HasAppliedVisualState ||
                             Math.Abs(state.LastAppliedOpacity - state.CurrentOpacity) > VisualOpacityUpdateThreshold;

        if (topChanged)
        {
            state.TranslateTransform!.Y = state.CurrentTop;
            state.LastAppliedTop = state.CurrentTop;
        }

        if (scaleChanged)
        {
            state.ScaleTransform!.ScaleX = state.CurrentScale;
            state.ScaleTransform.ScaleY = state.CurrentScale;
            state.LastAppliedScale = state.CurrentScale;
        }

        if (opacityChanged)
        {
            container.Opacity = state.CurrentOpacity;
            state.LastAppliedOpacity = state.CurrentOpacity;
        }

        state.HasAppliedVisualState = true;
    }

    private void TrimStaleStates(HashSet<Control> activeContainers)
    {
        var stale = _springStates.Keys.Where(x => !activeContainers.Contains(x)).ToArray();
        foreach (var container in stale)
            _springStates.Remove(container);
    }

    private void ResetFirstLayoutState()
    {
        _isFirstLayoutPass = true;
        _knownHeights.Clear();
        _springStates.Clear();
        _manualOffset = 0d;
        _manualOffsetTarget = 0d;
        _manualOffsetVelocity = 0d;
        _isManualOffsetReturning = false;
    }

    public void ForceSecondPassLayout()
    {
        if (!IsActive)
            return;

        InvalidateMeasure();
        InvalidateArrange();
        QueueLayoutUpdate();
        Dispatcher.Post(() =>
        {
            InvalidateMeasure();
            InvalidateArrange();
            QueueLayoutUpdate();
        }, DispatcherPriority.Render);
    }

    private sealed class SpringState
    {
        public bool IsInitialized { get; set; }

        public double CurrentTop { get; set; }

        public double TargetTop { get; set; }

        public double Velocity { get; set; }

        public double CurrentOpacity { get; set; } = 1d;

        public double TargetOpacity { get; set; } = 1d;

        public double CurrentScale { get; set; } = 1d;

        public double TargetScale { get; set; } = 1d;

        public double DelayRemainingSeconds { get; set; }

        public bool HasPendingTarget { get; set; }

        public double PendingTargetTop { get; set; }

        public double PendingTargetOpacity { get; set; }

        public double PendingTargetScale { get; set; } = 1d;

        public bool HasAppliedVisualState { get; set; }

        public double LastAppliedTop { get; set; }

        public double LastAppliedOpacity { get; set; }

        public double LastAppliedScale { get; set; } = 1d;

        public TranslateTransform? TranslateTransform { get; set; }

        public ScaleTransform? ScaleTransform { get; set; }

        public void EnsureTransform(Control container)
        {
            if (TranslateTransform != null && ScaleTransform != null)
                return;

            ScaleTransform = new ScaleTransform(CurrentScale, CurrentScale);
            TranslateTransform = new TranslateTransform(0d, CurrentTop);
            container.RenderTransform = new TransformGroup
            {
                Children =
                {
                    ScaleTransform,
                    TranslateTransform
                }
            };
            container.RenderTransformOrigin = RelativePoint.Center;
        }

        public void ScheduleTarget(double targetTop, double targetOpacity, double targetScale, double delaySeconds)
        {
            var currentRequestedTop = HasPendingTarget ? PendingTargetTop : TargetTop;
            var currentRequestedOpacity = HasPendingTarget ? PendingTargetOpacity : TargetOpacity;
            var currentRequestedScale = HasPendingTarget ? PendingTargetScale : TargetScale;

            if (Math.Abs(currentRequestedTop - targetTop) <= 0.5d &&
                Math.Abs(currentRequestedOpacity - targetOpacity) <= 0.01d &&
                Math.Abs(currentRequestedScale - targetScale) <= 0.001d)
                return;

            if (delaySeconds <= 0d)
            {
                TargetTop = targetTop;
                TargetOpacity = targetOpacity;
                TargetScale = targetScale;
                ClearPendingTarget();
                return;
            }

            QueueTarget(targetTop, targetOpacity, targetScale, delaySeconds);
        }

        public void QueueTarget(double targetTop, double targetOpacity, double targetScale, double delaySeconds)
        {
            PendingTargetTop = targetTop;
            PendingTargetOpacity = targetOpacity;
            PendingTargetScale = targetScale;
            DelayRemainingSeconds = Math.Max(0d, delaySeconds);
            HasPendingTarget = true;
        }

        public void UpdatePendingTarget(double dt)
        {
            if (!HasPendingTarget)
                return;

            DelayRemainingSeconds -= dt;
            if (DelayRemainingSeconds > 0d)
                return;

            TargetTop = PendingTargetTop;
            TargetOpacity = PendingTargetOpacity;
            TargetScale = PendingTargetScale;
            ClearPendingTarget();
        }

        public void ClearPendingTarget()
        {
            HasPendingTarget = false;
            DelayRemainingSeconds = 0d;
        }
    }
}
