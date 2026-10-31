using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace KugouAvaloniaPlayer.Controls;

/// <summary>
/// Application window chrome used by KA Music. It deliberately keeps only the
/// cross-platform window behavior that the player needs.
/// </summary>
public class KugouWindow : Window
{
    private static readonly TimeSpan MacDoubleClickInterval = TimeSpan.FromMilliseconds(500);
    private const double MacDoubleClickMaxDistance = 4;

    private readonly List<Action> _cleanupActions = [];
    private Control? _titleBar;
    private bool _chromeInitialized;
    private bool _suppressMacDoubleTapped;
    private DateTime _lastMacClickTime = DateTime.MinValue;
    private Point _lastMacClickPosition;
    private WindowState _previousVisibleWindowState = WindowState.Normal;

    protected KugouWindow()
    {
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowDecorations = OperatingSystem.IsWindows()
            ? WindowDecorations.Full
            : OperatingSystem.IsLinux()
                ? WindowDecorations.None
                : WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
    }

    /// <summary>
    /// Connects the app-owned title bar to native move, resize, zoom and snap behavior.
    /// Call once after InitializeComponent.
    /// </summary>
    protected void InitializeWindowChrome(Control titleBar, Button maximizeButton, Panel? resizeRoot = null)
    {
        if (_chromeInitialized)
            throw new InvalidOperationException("Window chrome has already been initialized.");

        _chromeInitialized = true;
        _titleBar = titleBar;

        if (OperatingSystem.IsMacOS())
        {
            DisableNativeTitleBarRole(titleBar);
            titleBar.DoubleTapped += OnMacTitleBarDoubleTapped;
            titleBar.AddHandler(
                PointerPressedEvent,
                OnMacTitleBarPointerPressed,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _cleanupActions.Add(() =>
            {
                titleBar.DoubleTapped -= OnMacTitleBarDoubleTapped;
                titleBar.RemoveHandler(PointerPressedEvent, OnMacTitleBarPointerPressed);
            });
        }
        else
        {
            titleBar.PointerPressed += OnTitleBarPointerPressed;
            titleBar.DoubleTapped += OnTitleBarDoubleTapped;
            _cleanupActions.Add(() =>
            {
                titleBar.PointerPressed -= OnTitleBarPointerPressed;
                titleBar.DoubleTapped -= OnTitleBarDoubleTapped;
            });
            
        }

        if (OperatingSystem.IsWindows())
            EnableWindowsSnapLayout(maximizeButton);

        if (OperatingSystem.IsLinux() && resizeRoot is not null)
            AddLinuxResizeGrips(resizeRoot);
    }

    /// <summary>
    /// Applies the user's Linux decoration preference without affecting other platforms.
    /// </summary>
    protected void ConfigureLinuxDecorations(bool useFullDecorations)
    {
        if (!OperatingSystem.IsLinux())
            return;

        ExtendClientAreaToDecorationsHint = false;
        WindowDecorations = useFullDecorations
            ? WindowDecorations.Full
            : WindowDecorations.BorderOnly;
    }

    public void ToggleFullScreen()
    {
        if (OperatingSystem.IsMacOS() && TryToggleMacFullScreen())
            return;

        WindowState = WindowState == WindowState.FullScreen
            ? _previousVisibleWindowState
            : WindowState.FullScreen;
    }

    public void ToggleMaximizeOrZoom()
    {
        var state = WindowState;
        if (!CanMaximize || state == WindowState.FullScreen)
            return;

        if (OperatingSystem.IsMacOS() && TryToggleMacZoom(state == WindowState.Maximized))
            return;

        WindowState = state == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == WindowStateProperty &&
            change.OldValue is WindowState oldState &&
            change.NewValue is WindowState newState &&
            oldState != WindowState.Minimized &&
            newState != WindowState.Minimized)
        {
            _previousVisibleWindowState = oldState;
        }

        base.OnPropertyChanged(change);
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var cleanup in _cleanupActions)
            cleanup();

        _cleanupActions.Clear();
        _titleBar = null;
        base.OnClosed(e);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.FullScreen ||
            !e.Properties.IsLeftButtonPressed ||
            IsFromUserElement(e.Source))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!IsFromUserElement(e.Source))
            ToggleMaximizeOrZoom();
    }

    private void OnMacTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Properties.IsLeftButtonPressed || IsFromUserElement(e.Source))
            return;

        var now = DateTime.UtcNow;
        var position = e.GetPosition(this);
        var deltaX = position.X - _lastMacClickPosition.X;
        var deltaY = position.Y - _lastMacClickPosition.Y;
        var distanceSquared = deltaX * deltaX + deltaY * deltaY;

        if (now - _lastMacClickTime <= MacDoubleClickInterval &&
            distanceSquared <= MacDoubleClickMaxDistance * MacDoubleClickMaxDistance)
        {
            _lastMacClickTime = DateTime.MinValue;
            _suppressMacDoubleTapped = true;
            e.Handled = true;
            e.PreventGestureRecognition();
            ToggleMaximizeOrZoom();
            return;
        }

        _lastMacClickTime = now;
        _lastMacClickPosition = position;
        OnTitleBarPointerPressed(sender, e);
    }

    private void OnMacTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsFromUserElement(e.Source))
            return;

        if (_suppressMacDoubleTapped)
        {
            _suppressMacDoubleTapped = false;
            return;
        }

        ToggleMaximizeOrZoom();
    }

    private bool IsFromUserElement(object? source)
    {
        for (var visual = source as Visual; visual is not null && visual != _titleBar; visual = visual.GetVisualParent())
        {
            if (WindowDecorationProperties.GetElementRole(visual) == WindowDecorationsElementRole.User)
                return true;
        }

        return false;
    }

    private static void DisableNativeTitleBarRole(Visual titleBar)
    {
        ClearRole(titleBar);
        foreach (var visual in titleBar.GetVisualDescendants())
            ClearRole(visual);

        static void ClearRole(Visual visual)
        {
            if (WindowDecorationProperties.GetElementRole(visual) == WindowDecorationsElementRole.TitleBar)
                WindowDecorationProperties.SetElementRole(visual, WindowDecorationsElementRole.None);
        }
    }

    private void AddLinuxResizeGrips(Panel root)
    {
        AddGrip("North", VerticalAlignment.Top, HorizontalAlignment.Stretch, StandardCursorType.SizeNorthSouth, false);
        AddGrip("South", VerticalAlignment.Bottom, HorizontalAlignment.Stretch, StandardCursorType.SizeNorthSouth, false);
        AddGrip("West", VerticalAlignment.Stretch, HorizontalAlignment.Left, StandardCursorType.SizeWestEast, false);
        AddGrip("East", VerticalAlignment.Stretch, HorizontalAlignment.Right, StandardCursorType.SizeWestEast, false);
        AddGrip("NorthWest", VerticalAlignment.Top, HorizontalAlignment.Left, StandardCursorType.TopLeftCorner, true);
        AddGrip("NorthEast", VerticalAlignment.Top, HorizontalAlignment.Right, StandardCursorType.TopRightCorner, true);
        AddGrip("SouthWest", VerticalAlignment.Bottom, HorizontalAlignment.Left, StandardCursorType.BottomLeftCorner, true);
        AddGrip("SouthEast", VerticalAlignment.Bottom, HorizontalAlignment.Right, StandardCursorType.BottomRightCorner, true);

        void AddGrip(
            string edge,
            VerticalAlignment verticalAlignment,
            HorizontalAlignment horizontalAlignment,
            StandardCursorType cursorType,
            bool isCorner)
        {
            var grip = new Border
            {
                Tag = edge,
                Background = Brushes.Transparent,
                VerticalAlignment = verticalAlignment,
                HorizontalAlignment = horizontalAlignment,
                Cursor = new Cursor(cursorType),
                ZIndex = 1000
            };

            if (isCorner)
            {
                grip.Width = 8;
                grip.Height = 8;
            }
            else if (verticalAlignment == VerticalAlignment.Stretch)
            {
                grip.Width = 6;
            }
            else
            {
                grip.Height = 6;
            }

            grip.PointerPressed += OnResizeGripPointerPressed;
            _cleanupActions.Add(() => grip.PointerPressed -= OnResizeGripPointerPressed);

            // 根节点是带 RowDefinitions 的 Grid 时，手柄必须横跨所有行，
            // 否则会默认落在第 0 行（如 MainWindow 的 44px 标题栏行），
            // 导致底边/左右边缩放手柄错位到窗口顶部。
            if (root is Grid gridRoot)
            {
                grip.SetValue(Grid.RowProperty, 0);
                grip.SetValue(Grid.RowSpanProperty, Math.Max(1, gridRoot.RowDefinitions.Count));
                grip.SetValue(Grid.ColumnProperty, 0);
                grip.SetValue(Grid.ColumnSpanProperty, Math.Max(1, gridRoot.ColumnDefinitions.Count));
            }

            root.Children.Add(grip);
        }
    }

    private void OnResizeGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!CanResize || WindowState != WindowState.Normal || sender is not Border { Tag: string edge })
            return;

        var windowEdge = edge switch
        {
            "North" => WindowEdge.North,
            "South" => WindowEdge.South,
            "West" => WindowEdge.West,
            "East" => WindowEdge.East,
            "NorthWest" => WindowEdge.NorthWest,
            "NorthEast" => WindowEdge.NorthEast,
            "SouthWest" => WindowEdge.SouthWest,
            "SouthEast" => WindowEdge.SouthEast,
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null)
        };

        BeginResizeDrag(windowEdge, e);
        e.Handled = true;
    }

    private void EnableWindowsSnapLayout(Button maximizeButton)
    {
        const uint wmNcHitTest = 0x0084;
        const int htClient = 1;
        const int htMaxButton = 9;
        var pointerOnButton = false;
        var pointerOverProperty = typeof(Button).GetProperty(nameof(Button.IsPointerOver));

        nint Hook(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled)
        {
            if (message != wmNcHitTest || !maximizeButton.IsVisible)
                return 0;

            var packed = IntPtr.Size == 4 ? lParam.ToInt32() : (int)(lParam.ToInt64() & 0xffffffff);
            var screenPoint = new PixelPoint((short)(packed & 0xffff), (short)(packed >> 16));
            // PointToClient accounts for both DPI scaling and the app's UI scale transform.
            var point = maximizeButton.PointToClient(screenPoint);
            var isInside = new Rect(maximizeButton.Bounds.Size).Contains(point);

            if (pointerOnButton != isInside && pointerOverProperty is not null)
            {
                pointerOnButton = isInside;
                pointerOverProperty.SetValue(maximizeButton, isInside);
            }

            if (!isInside)
                return 0;

            handled = true;
            return IsLeftMouseButtonDown() ? htClient : htMaxButton;
        }

        var callback = new Win32Properties.CustomWndProcHookCallback(Hook);
        Win32Properties.AddWndProcHookCallback(this, callback);
        _cleanupActions.Add(() => Win32Properties.RemoveWndProcHookCallback(this, callback));
    }

    private bool TryToggleMacZoom(bool isMaximized)
    {
        var handle = TryGetMacWindow();
        if (handle == IntPtr.Zero)
            return false;

        if (SendBool(handle, "isZoomed"))
        {
            SendVoid(handle, "performZoom:", handle);
            return true;
        }

        if (isMaximized)
            return false;

        SendVoid(handle, "performZoom:", handle);
        return true;
    }

    private bool TryToggleMacFullScreen()
    {
        var handle = TryGetMacWindow();
        if (handle == IntPtr.Zero)
            return false;

        SendVoid(handle, "toggleFullScreen:", IntPtr.Zero);
        return true;
    }

    private IntPtr TryGetMacWindow()
    {
        if (!OperatingSystem.IsMacOS())
            return IntPtr.Zero;

        var platformHandle = TryGetPlatformHandle();
        return platformHandle is { HandleDescriptor: "NSWindow" }
            ? platformHandle.Handle
            : IntPtr.Zero;
    }

    private static bool SendBool(IntPtr receiver, string selector) =>
        objc_msgSend_bool(receiver, sel_registerName(selector));

    private static void SendVoid(IntPtr receiver, string selector, IntPtr value) =>
        objc_msgSend_void_IntPtr(receiver, sel_registerName(selector), value);

    private static bool IsLeftMouseButtonDown() => (GetAsyncKeyState(1) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string name);

    [return: MarshalAs(UnmanagedType.I1)]
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr value);
}
