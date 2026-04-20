#if KUGOU_LINUX
using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services.DesktopLyric;

public sealed class DesktopLyricMousePassthroughService(ILogger<DesktopLyricMousePassthroughService> logger)
    : IDesktopLyricMousePassthroughService
{
    private const int ShapeInput = 2;
    private const int ShapeSet = 0;
    private const int Unsorted = 0;
    private bool _runtimeInfoLogged;

    private bool _x11Available = true;

    public bool IsSupported =>
        OperatingSystem.IsLinux() &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));

    public void Apply(Window window, bool enabled)
    {
        if (!IsSupported || !_x11Available) return;

        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle == null || platformHandle.Handle == IntPtr.Zero) return;

        LogRuntimeInfoOnce(platformHandle.HandleDescriptor);

        // Avalonia on Linux can run on Wayland or X11/XWayland.
        // Click-through is implemented only for X11 windows.
        if (!string.Equals(platformHandle.HandleDescriptor, "XID", StringComparison.OrdinalIgnoreCase))
            return;

        var display = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return;

            if (enabled)
            {
                XShapeCombineRectangles(
                    display,
                    platformHandle.Handle,
                    ShapeInput,
                    0,
                    0,
                    null,
                    0,
                    ShapeSet,
                    Unsorted);
            }
            else
            {
                var width = (ushort)Math.Clamp((int)Math.Ceiling(window.Bounds.Width), 1, ushort.MaxValue);
                var height = (ushort)Math.Clamp((int)Math.Ceiling(window.Bounds.Height), 1, ushort.MaxValue);
                var rects = new[] { new XRectangle(0, 0, width, height) };

                XShapeCombineRectangles(
                    display,
                    platformHandle.Handle,
                    ShapeInput,
                    0,
                    0,
                    rects,
                    rects.Length,
                    ShapeSet,
                    Unsorted);
            }

            _ = XFlush(display);
        }
        catch (DllNotFoundException)
        {
            _x11Available = false;
        }
        catch (EntryPointNotFoundException)
        {
            _x11Available = false;
        }
        finally
        {
            if (display != IntPtr.Zero)
                _ = XCloseDisplay(display);
        }
    }

    private void LogRuntimeInfoOnce(string? handleDescriptor)
    {
        if (_runtimeInfoLogged) return;
        _runtimeInfoLogged = true;

        var display = Environment.GetEnvironmentVariable("DISPLAY") ?? "(null)";
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "(null)";

        logger.LogInformation(
            "Desktop lyric mouse passthrough runtime: HandleDescriptor={HandleDescriptor}, DISPLAY={Display}, WAYLAND_DISPLAY={WaylandDisplay}. " +
            "Only XID (X11/XWayland) supports click-through.",
            handleDescriptor ?? "(null)",
            display,
            waylandDisplay);
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libXext.so.6")]
    private static extern void XShapeCombineRectangles(
        IntPtr display,
        IntPtr window,
        int shapeKind,
        int xOff,
        int yOff,
        [In] XRectangle[]? rectangles,
        int rectangleCount,
        int operation,
        int ordering);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct XRectangle(short x, short y, ushort width, ushort height)
    {
        public readonly short X = x;
        public readonly short Y = y;
        public readonly ushort Width = width;
        public readonly ushort Height = height;
    }
}
#endif