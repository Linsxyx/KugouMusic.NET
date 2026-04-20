using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace KugouAvaloniaPlayer.Controls;

public class MeasuredLyricScrollView : ItemsControl
{
    private const int StaggerRange = 5;
    private const int StaggerStepMs = 35;
    private const int EntranceStepMs = 25;
    private const double DefaultEstimatedLineHeight = 72;
    private const double EntranceRiseOffset = 110;
    private const double SpringStiffness = 0.105;
    private const double SpringDamping = 0.72;
    private const double OpacityResponse = 14.0;
    private const double SettleTopThreshold = 0.35;
    private const double SettleVelocityThreshold = 0.2;
    private const double SettleOpacityThreshold = 0.01;

    public static readonly StyledProperty<object?> ActiveItemProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, object?>(nameof(ActiveItem));

    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, double>(nameof(LineSpacing), 18);

    public static readonly StyledProperty<TimeSpan> ScrollDurationProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, TimeSpan>(nameof(ScrollDuration),
            TimeSpan.FromMilliseconds(420));

    public static readonly StyledProperty<double> WheelStepProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, double>(nameof(WheelStep), 80);

    public static readonly StyledProperty<double> ActiveAnchorRatioProperty =
        AvaloniaProperty.Register<MeasuredLyricScrollView, double>(nameof(ActiveAnchorRatio), 0.5);

    private readonly Dictionary<int, double> _knownHeights = new();
    private readonly Dictionary<Control, SpringState> _springStates = new();

    private readonly Stopwatch _frameStopwatch = new();
    private readonly DispatcherTimer _springTimer;
    private readonly DispatcherTimer _userScrollResetTimer;
    private INotifyCollectionChanged? _collectionChangedSource;
    private bool _deferredActiveItemUpdate;
    private bool _isFirstLayoutPass = true;
    private bool _isUserScrolling;
    private bool _layoutUpdateQueued;
    private int? _lockedActiveIndex;
    private double _manualOffset;

    public MeasuredLyricScrollView()
    {
        _springTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _springTimer.Tick += OnSpringTimerTick;

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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _springTimer.Stop();
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
            change.Property == ActiveAnchorRatioProperty)
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
        _manualOffset += e.Delta.Y * WheelStep;

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
        _manualOffset = 0;

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
        var staggerAnchorIndex = Math.Clamp(activeIndex + 1, 0, ItemCount - 1);

        var heights = new double[ItemCount];
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

            heights[i] = height;
        }

        // Keep the active lyric line on a configurable visual anchor rather than always hard-centering it.
        var centerY = Bounds.Height * Math.Clamp(ActiveAnchorRatio, 0.0, 1.0) + _manualOffset;
        var activeContainers = new HashSet<Control>();

        for (var i = 0; i < ItemCount; i++)
        {
            if (ContainerFromIndex(i) is not Control container) continue;

            activeContainers.Add(container);

            var targetCenterY = centerY;

            if (i < activeIndex)
                for (var j = i; j < activeIndex; j++)
                    targetCenterY -= heights[j] + LineSpacing;
            else if (i > activeIndex)
                for (var j = activeIndex; j < i; j++)
                    targetCenterY += heights[j] + LineSpacing;

            var targetTop = targetCenterY - heights[i] / 2;
            Canvas.SetLeft(container, 0);
            container.Width = Bounds.Width;

            var distance = Math.Abs(i - activeIndex);
            var targetOpacity = Math.Clamp(1 - distance * 0.16, 0.24, 1);
            UpdateSpringState(container, targetTop, targetOpacity, i, activeIndex);
        }

        TrimStaleStates(activeContainers);
        _isFirstLayoutPass = false;
        EnsureSpringTimerRunning();
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

    private void UpdateSpringState(Control container, double targetTop, double targetOpacity, int index, int activeIndex)
    {
        var isEntrance = _isFirstLayoutPass && !_isUserScrolling;
        var topDelay = isEntrance
            ? GetEntranceDelay(index, activeIndex)
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
            state.ClearPendingTarget();
            state.IsInitialized = true;
            Canvas.SetTop(container, targetTop);
            container.Opacity = targetOpacity;
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

            if (topDelay > TimeSpan.Zero)
            {
                state.TargetTop = state.CurrentTop;
                state.TargetOpacity = state.CurrentOpacity;
                state.QueueTarget(targetTop, targetOpacity, topDelay.TotalSeconds);
            }
            else
            {
                state.TargetTop = targetTop;
                state.TargetOpacity = targetOpacity;
                state.ClearPendingTarget();
            }

            Canvas.SetTop(container, state.CurrentTop);
            container.Opacity = state.CurrentOpacity;
            return;
        }

        state.ScheduleTarget(targetTop, targetOpacity, topDelay.TotalSeconds);
    }

    private void ResetFirstLayoutState()
    {
        _isFirstLayoutPass = true;
        _knownHeights.Clear();
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

    private void OnSpringTimerTick(object? sender, EventArgs e)
    {
        if (_springStates.Count == 0)
        {
            _springTimer.Stop();
            return;
        }

        var elapsed = _frameStopwatch.Elapsed;
        _frameStopwatch.Restart();
        var dt = Math.Clamp(elapsed.TotalSeconds, 1d / 240d, 1d / 20d);
        var frameFactor = dt * 60d;
        var opacityFactor = 1 - Math.Exp(-OpacityResponse * dt);
        var hasActiveMotion = false;

        foreach (var (container, state) in _springStates)
        {
            if (!state.IsInitialized) continue;

            state.UpdatePendingTarget(dt);

            var displacement = state.TargetTop - state.CurrentTop;
            state.Velocity += displacement * SpringStiffness * frameFactor;
            state.Velocity *= Math.Pow(SpringDamping, frameFactor);
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

            Canvas.SetTop(container, state.CurrentTop);
            container.Opacity = state.CurrentOpacity;
        }

        if (!hasActiveMotion)
            _springTimer.Stop();
    }

    private void EnsureSpringTimerRunning()
    {
        if (_isUserScrolling || _springStates.Count == 0) return;

        if (_springTimer.IsEnabled) return;

        _frameStopwatch.Restart();
        _springTimer.Start();
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

    private sealed class SpringState
    {
        public double CurrentOpacity;
        public double CurrentTop;
        public double DelayRemainingSeconds;
        public bool HasPendingTarget;
        public bool IsInitialized;
        public double PendingTargetOpacity;
        public double PendingTargetTop;
        public double TargetOpacity;
        public double TargetTop;
        public double Velocity;

        public void ScheduleTarget(double targetTop, double targetOpacity, double delaySeconds)
        {
            var currentRequestedTop = HasPendingTarget ? PendingTargetTop : TargetTop;
            var currentRequestedOpacity = HasPendingTarget ? PendingTargetOpacity : TargetOpacity;

            if (Math.Abs(currentRequestedTop - targetTop) <= 0.5 &&
                Math.Abs(currentRequestedOpacity - targetOpacity) <= 0.01)
                return;

            if (delaySeconds <= 0)
            {
                TargetTop = targetTop;
                TargetOpacity = targetOpacity;
                ClearPendingTarget();
                return;
            }

            QueueTarget(targetTop, targetOpacity, delaySeconds);
        }

        public void QueueTarget(double targetTop, double targetOpacity, double delaySeconds)
        {
            PendingTargetTop = targetTop;
            PendingTargetOpacity = targetOpacity;
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
            ClearPendingTarget();
        }

        public void ClearPendingTarget()
        {
            HasPendingTarget = false;
            DelayRemainingSeconds = 0;
        }
    }
}
