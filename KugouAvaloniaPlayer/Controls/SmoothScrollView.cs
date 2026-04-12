using System;
using Avalonia.Animation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Threading;

namespace KugouAvaloniaPlayer.Controls;

public class SmoothScrollView : ItemsControl
{
    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<SmoothScrollView, object?>(nameof(SelectedItem));

    public static readonly StyledProperty<double> CurrentOffsetProperty =
        AvaloniaProperty.Register<SmoothScrollView, double>(nameof(CurrentOffset));

    public static readonly StyledProperty<TimeSpan> ScrollDurationProperty =
        AvaloniaProperty.Register<SmoothScrollView, TimeSpan>(nameof(ScrollDuration), TimeSpan.FromMilliseconds(600));

    private readonly DispatcherTimer _userScrollResetTimer;
    private bool _isUserScrolling;
    private bool _waveUpdateQueued;


    private ItemsPresenter? _itemsPresenter;

    public SmoothScrollView()
    {
        _userScrollResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _userScrollResetTimer.Tick += OnUserScrollTimeout;
    }

    protected override Type StyleKeyOverride => typeof(SmoothScrollView);

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public double CurrentOffset
    {
        get => GetValue(CurrentOffsetProperty);
        set => SetValue(CurrentOffsetProperty, value);
    }

    public TimeSpan ScrollDuration
    {
        get => GetValue(ScrollDurationProperty);
        set => SetValue(ScrollDurationProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _itemsPresenter = e.NameScope.Find<ItemsPresenter>("PART_ItemsPresenter");
    }


    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedItemProperty)
        {
            if (!_isUserScrolling)
            {
                SetCurrentValue(ScrollDurationProperty, TimeSpan.FromMilliseconds(600));
                UpdateScrollPosition();
            }

            QueueUpdateWaveTransforms();
        }
        else if (change.Property == BoundsProperty)
        {
            if (!_isUserScrolling) UpdateScrollPosition();
            QueueUpdateWaveTransforms();
        }
        else if (change.Property == CurrentOffsetProperty)
        {
            QueueUpdateWaveTransforms();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_itemsPresenter == null) return;

        _isUserScrolling = true;

        SetCurrentValue(ScrollDurationProperty, TimeSpan.FromMilliseconds(50));

        _userScrollResetTimer.Stop();
        _userScrollResetTimer.Start();

        var delta = e.Delta.Y * 100;
        var targetOffset = CurrentOffset + delta;

        var viewportHeight = Bounds.Height;

        var contentHeight = GetTrueContentHeight();

        var maxOffset = viewportHeight / 2;

        var minOffset = viewportHeight / 2 - contentHeight;

        if (contentHeight < viewportHeight) minOffset = maxOffset = (viewportHeight - contentHeight) / 2;

        CurrentOffset = Math.Clamp(targetOffset, minOffset, maxOffset);
        e.Handled = true;
    }

    private void OnUserScrollTimeout(object? sender, EventArgs e)
    {
        _userScrollResetTimer.Stop();
        _isUserScrolling = false;

        SetCurrentValue(ScrollDurationProperty, TimeSpan.FromMilliseconds(600));
        UpdateScrollPosition();
        QueueUpdateWaveTransforms();
    }

    private void UpdateScrollPosition()
    {
        if (SelectedItem == null || _itemsPresenter == null) return;

        var container = ContainerFromItem(SelectedItem);
        if (container == null) return;

        var itemBounds = container.Bounds;
        var viewportHeight = Bounds.Height;

        var itemCenterY = itemBounds.Y + itemBounds.Height / 2;
        var targetOffset = viewportHeight / 2 - itemCenterY;

        CurrentOffset = targetOffset;
    }

    private void QueueUpdateWaveTransforms()
    {
        if (_waveUpdateQueued) return;

        _waveUpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _waveUpdateQueued = false;
            UpdateWaveTransforms();
        }, DispatcherPriority.Render);
    }

    private void UpdateWaveTransforms()
    {
        if (ItemCount <= 0) return;

        var selectedIndex = GetSelectedIndex();
        for (var i = 0; i < ItemCount; i++)
        {
            if (ContainerFromIndex(i) is not Control container) continue;

            EnsureItemTransitions(container);

            var distance = selectedIndex >= 0 ? i - selectedIndex : 0;
            var absDistance = Math.Abs(distance);

            // 通过衰减正弦让临近行产生自然上下波动，避免突兀跳变。
            var wave = Math.Sin(distance * 0.9) * 11 * Math.Exp(-absDistance * 0.35);
            var centerLift = -4 * Math.Exp(-absDistance * 0.55);
            var targetY = wave + centerLift;
            var targetOpacity = selectedIndex >= 0
                ? Math.Clamp(1 - absDistance * 0.16, 0.24, 1)
                : 1;

            if (container.RenderTransform is not TranslateTransform translate)
            {
                translate = new TranslateTransform();
                container.RenderTransform = translate;
            }

            translate.Y = targetY;
            container.Opacity = targetOpacity;
        }
    }

    private static void EnsureItemTransitions(Control container)
    {
        if (container.Transitions?.Count > 0) return;

        container.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(380)
            },
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(420)
            }
        };
    }

    private int GetSelectedIndex()
    {
        if (SelectedItem == null) return -1;

        var selectedContainer = ContainerFromItem(SelectedItem);
        if (selectedContainer == null) return -1;

        for (var i = 0; i < ItemCount; i++)
        {
            if (ReferenceEquals(ContainerFromIndex(i), selectedContainer))
                return i;
        }

        return -1;
    }

    private double GetTrueContentHeight()
    {
        if (ItemCount == 0) return 0;

        var lastContainer = ContainerFromIndex(ItemCount - 1);
        if (lastContainer != null) return lastContainer.Bounds.Bottom;

        if (_itemsPresenter?.Panel != null) return _itemsPresenter.Panel.Bounds.Height;

        return _itemsPresenter?.Bounds.Height ?? 0;
    }
}
