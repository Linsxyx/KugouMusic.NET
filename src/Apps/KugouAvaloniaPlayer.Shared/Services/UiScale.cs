using System;
using Avalonia.Platform;

namespace KugouAvaloniaPlayer.Services;

public sealed record UiScaleOption(double Value, string Label);

/// <summary>
///     主界面缩放。系统 DPI 缩放由 Avalonia 处理；这里只在逻辑分辨率很大的屏幕上
///     （如 macOS 选择"更多空间"、Windows 2K/4K 屏用 100%~150% 缩放）额外放大界面。
/// </summary>
public static class UiScale
{
    public const double Auto = 0;
    public const double Min = 1.0;
    public const double Max = 2.0;

    // 界面按约 1728×1080 的逻辑分辨率设计，超出部分才自动放大。
    private const double ReferenceWidth = 1728;
    private const double ReferenceHeight = 1080;
    private const double AutoStep = 0.05;

    public static UiScaleOption[] Options { get; } =
    [
        new(Auto, "自动"),
        new(1.0, "100%"),
        new(1.1, "110%"),
        new(1.25, "125%"),
        new(1.5, "150%"),
        new(1.75, "175%"),
        new(2.0, "200%"),
    ];

    /// <summary>主窗口当前生效的缩放比例。</summary>
    public static double Current
    {
        get => _current;
        set
        {
            if (Math.Abs(_current - value) < 0.001)
                return;

            _current = value;
            CurrentChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static event EventHandler? CurrentChanged;

    private static double _current = 1;

    public static double Normalize(double value)
    {
        return double.IsFinite(value) && value > 0 ? Math.Clamp(value, Min, Max) : Auto;
    }

    public static UiScaleOption FindOption(double value)
    {
        var normalized = Normalize(value);
        foreach (var option in Options)
        {
            if (Math.Abs(option.Value - normalized) < 0.001)
                return option;
        }

        return Options[0];
    }

    /// <summary>
    ///     解析设置值；自动模式按窗口所在屏幕的逻辑尺寸推算。
    /// </summary>
    public static double Resolve(double setting, Screen? screen)
    {
        var normalized = Normalize(setting);
        return normalized == Auto ? ResolveAuto(screen) : normalized;
    }

    private static double ResolveAuto(Screen? screen)
    {
        if (screen is null || screen.Scaling <= 0)
            return Min;

        var logicalWidth = screen.Bounds.Width / screen.Scaling;
        var logicalHeight = screen.Bounds.Height / screen.Scaling;
        var scale = Math.Min(logicalWidth / ReferenceWidth, logicalHeight / ReferenceHeight);

        // 向下取整到固定档位，避免细微的尺寸差异导致界面反复缩放。
        scale = Math.Floor(scale / AutoStep) * AutoStep;
        return Math.Clamp(scale, Min, Max);
    }
}
