using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace KugouAvaloniaPlayer.Controls;

public class SmoothScrollView : ItemsControl
{
    protected override Type StyleKeyOverride => typeof(SmoothScrollView);
    
    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<SmoothScrollView, object?>(nameof(SelectedItem));

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }
    
    public static readonly StyledProperty<double> CurrentOffsetProperty =
        AvaloniaProperty.Register<SmoothScrollView, double>(nameof(CurrentOffset));

    public double CurrentOffset
    {
        get => GetValue(CurrentOffsetProperty);
        set => SetValue(CurrentOffsetProperty, value);
    }
    
    public static readonly StyledProperty<TimeSpan> ScrollDurationProperty =
        AvaloniaProperty.Register<SmoothScrollView, TimeSpan>(nameof(ScrollDuration), TimeSpan.FromMilliseconds(600));

    public TimeSpan ScrollDuration
    {
        get => GetValue(ScrollDurationProperty);
        set => SetValue(ScrollDurationProperty, value);
    }
    

    private ItemsPresenter? _itemsPresenter;
    private bool _isUserScrolling; 
    private readonly DispatcherTimer _userScrollResetTimer; 

    public SmoothScrollView()
    {
        _userScrollResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _userScrollResetTimer.Tick += OnUserScrollTimeout;
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
        }
        else if (change.Property == BoundsProperty)
        {
            if (!_isUserScrolling) UpdateScrollPosition();
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
        
        double delta = e.Delta.Y * 100; 
        double targetOffset = CurrentOffset + delta;

        double viewportHeight = Bounds.Height;
        
        double contentHeight = GetTrueContentHeight();
        
        double maxOffset = viewportHeight / 2;
        
        double minOffset = (viewportHeight / 2) - contentHeight;
        
        if (contentHeight < viewportHeight)
        {
            minOffset = maxOffset = (viewportHeight - contentHeight) / 2;
        }

        CurrentOffset = Math.Clamp(targetOffset, minOffset, maxOffset);
        e.Handled = true; 
    }
    
    private void OnUserScrollTimeout(object? sender, EventArgs e)
    {
        _userScrollResetTimer.Stop();
        _isUserScrolling = false;
        
        SetCurrentValue(ScrollDurationProperty, TimeSpan.FromMilliseconds(600)); 
        UpdateScrollPosition();
    }
    
    private void UpdateScrollPosition()
    {
        if (SelectedItem == null || _itemsPresenter == null) return;

        var container = ContainerFromItem(SelectedItem);
        if (container == null) return;

        var itemBounds = container.Bounds;
        var viewportHeight = Bounds.Height;
        
        var itemCenterY = itemBounds.Y + (itemBounds.Height / 2);
        var targetOffset = (viewportHeight / 2) - itemCenterY;

        CurrentOffset = targetOffset;
    }
    
    private double GetTrueContentHeight()
    {
        if (ItemCount == 0) return 0;
        
        var lastContainer = ContainerFromIndex(ItemCount - 1);
        if (lastContainer != null)
        {
            return lastContainer.Bounds.Bottom;
        }
        
        if (_itemsPresenter?.Panel != null)
        {
            return _itemsPresenter.Panel.Bounds.Height;
        }
        
        return _itemsPresenter?.Bounds.Height ?? 0;
    }
}