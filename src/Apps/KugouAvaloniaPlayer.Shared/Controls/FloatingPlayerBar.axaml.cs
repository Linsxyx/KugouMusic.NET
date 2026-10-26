using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

// 特色主题下可伸缩悬浮播放进度条控件代码隐藏，负责鼠标悬浮、延时折叠以及拖拽滑块时的展开保护逻辑。

namespace KugouAvaloniaPlayer.Controls;

public partial class FloatingPlayerBar : UserControl
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<FloatingPlayerBar, bool>(nameof(IsExpanded), defaultValue: false);

    private readonly DispatcherTimer _collapseTimer;
    private bool _isDraggingSlider;
    private bool _isPointerInside;

    public FloatingPlayerBar()
    {
        InitializeComponent();

        _collapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _collapseTimer.Tick += OnCollapseTimerTick;

        // 在隧道路由中监听，避免 Thumb 处理事件后漏掉松开；每个事件只处理一次。
        ProgressSlider.AddHandler(InputElement.PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        ProgressSlider.AddHandler(InputElement.PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        ProgressSlider.AddHandler(InputElement.PointerCaptureLostEvent, OnSliderPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _collapseTimer.Stop();
        _isPointerInside = false;
        _isDraggingSlider = false;
        IsExpanded = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty)
        {
            var expanded = change.GetNewValue<bool>();
            PseudoClasses.Set(":expanded", expanded);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _isPointerInside = true;
        _collapseTimer.Stop();
        IsExpanded = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isPointerInside = false;
        // 如果正在拖动滑块，则保持展开，待松开后再触发收起
        if (!_isDraggingSlider)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    /// <summary>
    /// 处理滑块按下事件，标记处于拖拽中以防手势轻微移出边界时意外收起。
    /// </summary>
    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ProgressSlider).Properties.IsLeftButtonPressed)
            return;

        _isDraggingSlider = true;
        _collapseTimer.Stop();
        IsExpanded = true;
    }

    /// <summary>
    /// 处理滑块松开事件，结束拖拽并在光标离开时启动收起计时器。
    /// </summary>
    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDraggingSlider = false;
        if (!_isPointerInside)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void OnSliderPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDraggingSlider = false;
        if (!_isPointerInside)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void OnCollapseTimerTick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (!_isPointerInside && !_isDraggingSlider)
        {
            IsExpanded = false;
        }
    }
}
