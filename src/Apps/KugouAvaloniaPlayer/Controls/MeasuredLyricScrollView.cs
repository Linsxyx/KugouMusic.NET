using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace KugouAvaloniaPlayer.Controls;

public class MeasuredLyricScrollView : ItemsControl
{
    private const int StaggerRange = 10;
    private const int StaggerStepMs = 20;
    private const int EntranceStepMs = 12;
    private const double EntranceRiseOffset = 48;
    private const double BaseSpringStiffness = 0.063;
    private const double BaseSpringDamping = 0.72;
    private const double BaseScrollDurationMs = 420;
    private const double ManualOffsetReturnStiffness = 0.052;
    private const double ManualOffsetReturnDamping = 0.78;
    private const double OpacityResponse = 18.0;
    private const double SettleTopThreshold = 0.22;
    private const double SettleVelocityThreshold = 0.12;
    private const double SettleManualOffsetThreshold = 0.35;
    private const double SettleManualVelocityThreshold = 0.2;
    private const double SettleOpacityThreshold = 0.008;

    private const double DefaultEstimatedLineHeight = 72;
    
    public static readonly StyledProperty<object?> ActiveItemProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, object?>(nameof(ActiveItem));

    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, double>(nameof(LineSpacing), 14);

    public static readonly StyledProperty<TimeSpan> ScrollDurationProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, TimeSpan>(nameof(ScrollDuration),
            TimeSpan.FromMilliseconds(420));

    public static readonly StyledProperty<double> WheelStepProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, double>(nameof(WheelStep), 80);

    public static readonly StyledProperty<double> ActiveAnchorRatioProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, double>(nameof(ActiveAnchorRatio), 0.35);

    public static readonly StyledProperty<double> EdgeFadeRatioProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, double>(nameof(EdgeFadeRatio), 0.15);
    
    public static readonly StyledProperty<bool> EnableScaleProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, bool>(nameof(EnableScale), true);

    public static readonly StyledProperty<double> InactiveScaleProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, double>(nameof(InactiveScale), 0.97);


    private readonly Dictionary<int, double> _knownHeights = new();
    private readonly Dictionary<Control, SpringState> _springStates = new();
    private readonly HashSet<Control> _activeContainers = new();

    private readonly DispatcherTimer _userScrollResetTimer;
    private double[] _lineCenters = [];
    private double[] _lineHeights = [];
    private INotifyCollectionChanged? _collectionChangedSource;
    private bool _animationFrameQueued;
    private bool _deferredActiveItemUpdate;
    private bool _hasLastFrameTimestamp;
    private bool _isFirstLayoutPass = true;
    private bool _isManualOffsetReturning;
    private bool _isUserScrolling;
    private bool _layoutUpdateQueued;
    private TimeSpan _lastFrameTimestamp;
    private int? _lockedActiveIndex;
    private double _manualOffset;
    private double _manualOffsetTarget;
    private double _manualOffsetVelocity;

    public MeasuredLyricScrollView()
    {
        _userScrollResetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _userScrollResetTimer.Tick += OnUserScrollTimeout;

        LayoutUpdated += OnLayoutUpdated;
    }

    protected override Type StyleKeyOverride => typeof(MeasuredLyricScrollView);

    public object? ActiveItem
    {
        get => GetValue(ActiveItemProperty);
        set => SetValue(ActiveItemProperty, value);
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

    public double WheelStep
    {
        get => GetValue(WheelStepProperty);
        set => SetValue(WheelStepProperty, value);
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

    public double InactiveScale
    {
        get => GetValue(InactiveScaleProperty);
        set => SetValue(InactiveScaleProperty, value);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _animationFrameQueued = false;
        _hasLastFrameTimestamp = false;
        _userScrollResetTimer.Stop();
        UnhookCollectionChanged();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ActiveItemProperty)
        {
            if (_isUserScrolling)
            {
                _deferredActiveItemUpdate = true;
                return;
            }

            QueueLayoutUpdate();
            return;
        }

        if (change.Property == BoundsProperty ||
            change.Property == LineSpacingProperty ||
            change.Property == ScrollDurationProperty ||
            change.Property == ActiveAnchorRatioProperty ||
            change.Property == EdgeFadeRatioProperty)
        {
            QueueLayoutUpdate();
            return;
        }

        if (change.Property == ItemsSourceProperty)
        {
            HookCollectionChanged(change.NewValue);
            ResetFirstLayoutState();
            QueueLayoutUpdate();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (ItemCount == 0) return;

        if (!_isUserScrolling)
            _lockedActiveIndex = GetActiveIndex();

        _isUserScrolling = true;
        _isManualOffsetReturning = false;

        var wheelOffset = e.Delta.Y * WheelStep;
        _manualOffset += wheelOffset;
        _manualOffsetTarget = _manualOffset;
        _manualOffsetVelocity = wheelOffset * 0.35;

        _userScrollResetTimer.Stop();
        _userScrollResetTimer.Start();

        QueueLayoutUpdate();
        e.Handled = true;
    }

    private void OnUserScrollTimeout(object? sender, EventArgs e)
    {
        _userScrollResetTimer.Stop();
        _isUserScrolling = false;
        _lockedActiveIndex = null;
        _manualOffsetTarget = 0;
        _isManualOffsetReturning = Math.Abs(_manualOffset) > SettleManualOffsetThreshold;

        if (_deferredActiveItemUpdate)
            _deferredActiveItemUpdate = false;

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
        if (_collectionChangedSource == null) return;

        _collectionChangedSource.CollectionChanged -= OnItemsSourceCollectionChanged;
        _collectionChangedSource = null;
    }

    private void OnItemsSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResetFirstLayoutState();
        QueueLayoutUpdate();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_isUserScrolling) return;
        if (RefreshMeasuredHeights())
            QueueLayoutUpdate();
    }

    private void QueueLayoutUpdate()
    {
        if (_layoutUpdateQueued) return;

        _layoutUpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _layoutUpdateQueued = false;
            ApplyMeasuredLayout();
        }, DispatcherPriority.Render);
    }

    private void ApplyMeasuredLayout()
    {
        if (ItemCount == 0 || Bounds.Height <= 0 || Bounds.Width <= 0) return;

        var activeIndex = _isUserScrolling
            ? _lockedActiveIndex ?? GetActiveIndex()
            : GetActiveIndex();
        if (activeIndex < 0 || activeIndex >= ItemCount)
            activeIndex = 0;

        EnsureLayoutBuffers(ItemCount);

        var naturalTop = 0d;
        for (var i = 0; i < ItemCount; i++)
        {
            if (ContainerFromIndex(i) is not Control container) continue;

            var height = container.Bounds.Height;
            if (height <= 0)
                height = container.DesiredSize.Height;
            if (height <= 0)
                height = _knownHeights.GetValueOrDefault(i, DefaultEstimatedLineHeight);
            else
                _knownHeights[i] = height;

            _lineHeights[i] = height;
            _lineCenters[i] = naturalTop + height / 2;
            naturalTop += height + LineSpacing;
        }

        // Keep the active lyric line on a configurable visual anchor rather than always hard-centering it.
        var centerY = Bounds.Height * Math.Clamp(ActiveAnchorRatio, 0.0, 1.0) + _manualOffset;
        var activeNaturalCenter = _lineCenters[activeIndex];
        _activeContainers.Clear();

        for (var i = 0; i < ItemCount; i++)
        {
            if (ContainerFromIndex(i) is not Control container) continue;

            _activeContainers.Add(container);

            var height = _lineHeights[i];
            var targetTop = centerY + (_lineCenters[i] - activeNaturalCenter) - height / 2;
            Canvas.SetLeft(container, 0);
            Canvas.SetTop(container, 0);
            container.Width = Bounds.Width;

            var distance = Math.Abs(i - activeIndex);
            double targetOpacity;
            if (distance == 0)
                targetOpacity = 1.0;
            else if (distance == 1)
                targetOpacity = 0.88;
            else if (distance == 2)
                targetOpacity = 0.72;
            else
                targetOpacity = Math.Clamp(0.58 - (distance - 3) * 0.10, 0.16, 1.0);

            targetOpacity *= CalculateEdgeFadeFactor(targetTop, height);

            double targetScale = 1.0;
            if (EnableScale && distance > 0)
                targetScale = InactiveScale;

            UpdateSpringState(container, targetTop, targetOpacity, targetScale, i, activeIndex);
        }

        TrimStaleStates(_activeContainers);
        _isFirstLayoutPass = false;
        EnsureAnimationFrameRunning();
    }

    private double CalculateEdgeFadeFactor(double top, double height)
    {
        var fadeLength = Bounds.Height * Math.Clamp(EdgeFadeRatio, 0, 0.45);
        if (fadeLength <= 0)
            return 1;

        var center = top + height / 2;
        var topFactor = SmoothStep(Math.Clamp(center / fadeLength, 0, 1));
        var bottomFactor = SmoothStep(Math.Clamp((Bounds.Height - center) / fadeLength, 0, 1));
        return Math.Min(topFactor, bottomFactor);
    }

    private static double SmoothStep(double value)
    {
        return value * value * (3 - 2 * value);
    }

    private void EnsureLayoutBuffers(int itemCount)
    {
        if (_lineHeights.Length >= itemCount && _lineCenters.Length >= itemCount) return;

        var capacity = Math.Max(itemCount, Math.Max(_lineHeights.Length, 8) * 2);
        _lineHeights = new double[capacity];
        _lineCenters = new double[capacity];
    }

    private int GetActiveIndex()
    {
        if (ActiveItem == null) return -1;

        var index = ItemsView.IndexOf(ActiveItem);
        if (index >= 0) return index;

        if (ItemsSource is IList list) return list.IndexOf(ActiveItem);

        return -1;
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

    private void UpdateSpringState(Control container, double targetTop, double targetOpacity, double targetScale, int index, int activeIndex)
    {
        var isEntrance = _isFirstLayoutPass && !_isUserScrolling;
        var topDelay = isEntrance
            ? GetEntranceDelay(index, activeIndex)
            : _isManualOffsetReturning
                ? TimeSpan.Zero
            : GetTopTransitionDelay(index, activeIndex + 1);
        if (!_springStates.TryGetValue(container, out var state))
        {
            state = new SpringState();
            _springStates[container] = state;
        }

        if (_isUserScrolling)
        {
            state.CurrentTop = targetTop;
            state.TargetTop = targetTop;
            state.Velocity = 0;
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
                ? targetTop + EntranceRiseOffset + Math.Abs(index - activeIndex) * 8
                : targetTop;
            state.Velocity = 0;
            state.CurrentOpacity = isEntrance ? 0 : targetOpacity;
            state.IsInitialized = true;

            state.CurrentOpacity = isEntrance ? 0 : targetOpacity;
            state.CurrentScale = isEntrance ? 0.985 : targetScale;

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

    private void ResetFirstLayoutState()
    {
        _isFirstLayoutPass = true;
        _knownHeights.Clear();
        _manualOffset = 0;
        _manualOffsetTarget = 0;
        _manualOffsetVelocity = 0;
        _isManualOffsetReturning = false;
    }

    public void ForceSecondPassLayout()
    {
        InvalidateMeasure();
        InvalidateArrange();
        QueueLayoutUpdate();
        Dispatcher.UIThread.Post(() =>
        {
            InvalidateMeasure();
            InvalidateArrange();
            QueueLayoutUpdate();
        }, DispatcherPriority.Render);
    }

    private bool RefreshMeasuredHeights()
    {
        var hasChanged = false;
        for (var i = 0; i < ItemCount; i++)
        {
            if (ContainerFromIndex(i) is not Control container) continue;

            var height = container.Bounds.Height;
            if (height <= 0)
                height = container.DesiredSize.Height;
            if (height <= 0) continue;

            if (!_knownHeights.TryGetValue(i, out var knownHeight) || Math.Abs(knownHeight - height) > 0.5)
            {
                _knownHeights[i] = height;
                hasChanged = true;
            }
        }

        return hasChanged;
    }

    private void OnAnimationFrame(TimeSpan timestamp)
    {
        _animationFrameQueued = false;

        if (TopLevel.GetTopLevel(this) == null)
        {
            _hasLastFrameTimestamp = false;
            return;
        }

        if (_springStates.Count == 0 && !_isManualOffsetReturning) return;

        var elapsed = _hasLastFrameTimestamp
            ? timestamp - _lastFrameTimestamp
            : TimeSpan.FromSeconds(1d / 60d);
        _lastFrameTimestamp = timestamp;
        _hasLastFrameTimestamp = true;

        var dt = Math.Clamp(elapsed.TotalSeconds, 1d / 240d, 1d / 20d);
        var frameFactor = dt * 60d;
        var durationFactor = Math.Clamp(BaseScrollDurationMs / Math.Max(120, ScrollDuration.TotalMilliseconds), 0.55, 2.2);
        var springStiffness = BaseSpringStiffness * durationFactor * durationFactor;
        var springDamping = Math.Pow(BaseSpringDamping, durationFactor);
        var opacityFactor = 1 - Math.Exp(-OpacityResponse * dt);
        var hasActiveMotion = UpdateManualOffset(frameFactor);
        if (hasActiveMotion)
            ApplyMeasuredLayout();

        foreach (var (container, state) in _springStates)
        {
            if (!state.IsInitialized) continue;

            state.UpdatePendingTarget(dt);

            var displacement = state.TargetTop - state.CurrentTop;
            state.Velocity += displacement * springStiffness * frameFactor;
            state.Velocity *= Math.Pow(springDamping, frameFactor);
            state.CurrentTop += state.Velocity * frameFactor;

            if (Math.Abs(state.TargetTop - state.CurrentTop) <= SettleTopThreshold &&
                Math.Abs(state.Velocity) <= SettleVelocityThreshold)
            {
                state.CurrentTop = state.TargetTop;
                state.Velocity = 0;
            }
            else
            {
                hasActiveMotion = true;
            }

            state.CurrentOpacity += (state.TargetOpacity - state.CurrentOpacity) * opacityFactor;
            if (Math.Abs(state.TargetOpacity - state.CurrentOpacity) <= SettleOpacityThreshold)
            {
                state.CurrentOpacity = state.TargetOpacity;
            }
            else
            {
                hasActiveMotion = true;
            }

            state.CurrentScale += (state.TargetScale - state.CurrentScale) * opacityFactor;
            if (Math.Abs(state.TargetScale - state.CurrentScale) <= 0.001)
            {
                state.CurrentScale = state.TargetScale;
            }
            else
            {
                hasActiveMotion = true;
            }

            ApplyVisualState(container, state);
        }

        if (!hasActiveMotion)
        {
            _hasLastFrameTimestamp = false;
            return;
        }

        RequestNextAnimationFrame();
    }

    private void EnsureAnimationFrameRunning()
    {
        if (_isUserScrolling || (_springStates.Count == 0 && !_isManualOffsetReturning)) return;

        if (_animationFrameQueued) return;

        RequestNextAnimationFrame();
    }

    private void RequestNextAnimationFrame()
    {
        if (_animationFrameQueued) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        _animationFrameQueued = true;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private bool UpdateManualOffset(double frameFactor)
    {
        if (!_isManualOffsetReturning) return false;

        var displacement = _manualOffsetTarget - _manualOffset;
        _manualOffsetVelocity += displacement * ManualOffsetReturnStiffness * frameFactor;
        _manualOffsetVelocity *= Math.Pow(ManualOffsetReturnDamping, frameFactor);
        _manualOffset += _manualOffsetVelocity * frameFactor;

        if (Math.Abs(_manualOffsetTarget - _manualOffset) <= SettleManualOffsetThreshold &&
            Math.Abs(_manualOffsetVelocity) <= SettleManualVelocityThreshold)
        {
            _manualOffset = _manualOffsetTarget;
            _manualOffsetVelocity = 0;
            _isManualOffsetReturning = false;
            return true;
        }

        return true;
    }

    private void TrimStaleStates(HashSet<Control> activeContainers)
    {
        if (_springStates.Count == activeContainers.Count) return;

        var staleContainers = new List<Control>();
        foreach (var container in _springStates.Keys)
        {
            if (!activeContainers.Contains(container))
                staleContainers.Add(container);
        }

        foreach (var staleContainer in staleContainers)
            _springStates.Remove(staleContainer);
    }

    private static void ApplyVisualState(Control container, SpringState state)
    {
        state.EnsureTransform(container);

        state.TranslateTransform!.Y = state.CurrentTop;
        state.ScaleTransform!.ScaleX = state.CurrentScale;
        state.ScaleTransform.ScaleY = state.CurrentScale;
        container.Opacity = state.CurrentOpacity;
    }

    private sealed class SpringState
    {
        public double CurrentOpacity;
        public double CurrentTop;
        public double CurrentScale = 1.0;

        public double DelayRemainingSeconds;
        public bool HasPendingTarget;
        public bool IsInitialized;

        public double PendingTargetOpacity;
        public double PendingTargetTop;
        public double PendingTargetScale = 1.0;

        public double TargetOpacity;
        public double TargetTop;
        public double TargetScale = 1.0;

        public double Velocity;

        public TranslateTransform? TranslateTransform;
        public ScaleTransform? ScaleTransform;

        public void EnsureTransform(Control container)
        {
            if (TranslateTransform != null && ScaleTransform != null)
                return;

            ScaleTransform = new ScaleTransform(CurrentScale, CurrentScale);
            TranslateTransform = new TranslateTransform(0, CurrentTop);
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

            if (Math.Abs(currentRequestedTop - targetTop) <= 0.5 &&
                Math.Abs(currentRequestedOpacity - targetOpacity) <= 0.01 &&
                Math.Abs(currentRequestedScale - targetScale) <= 0.001)
                return;

            if (delaySeconds <= 0)
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
            DelayRemainingSeconds = Math.Max(0, delaySeconds);
            HasPendingTarget = true;
        }

        public void UpdatePendingTarget(double dt)
        {
            if (!HasPendingTarget) return;

            DelayRemainingSeconds -= dt;
            if (DelayRemainingSeconds > 0) return;

            TargetTop = PendingTargetTop;
            TargetOpacity = PendingTargetOpacity;
            TargetScale = PendingTargetScale;
            ClearPendingTarget();
        }

        public void ClearPendingTarget()
        {
            HasPendingTarget = false;
            DelayRemainingSeconds = 0;
        }
    }
}
