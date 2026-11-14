using System.Runtime.InteropServices;

namespace KaMusic.Cli.Graphics;

// 终端图形能力：图片协议、颜色深度与字符格像素尺寸的探测。可通过环境变量 KGTEST_IMAGE / KGTEST_COLOR /
// KGTEST_CELL_SIZE 覆盖。

internal enum TerminalImageMode
{
    Auto,
    Kitty,
    ITerm,
    Sixel,
    Blocks,
    Off
}

/// <summary>
///     终端支持的 ANSI 颜色深度。Linux 虚拟终端（TERM=linux）与 vt100/console 等只有 16 色。
/// </summary>
internal enum TerminalColorDepth
{
    Ansi16,
    Ansi256,
    TrueColor
}

internal static class TerminalCapabilities
{
    /// <summary>
    ///     根据环境变量推断终端支持的最佳图片协议。
    /// </summary>
    public static TerminalImageMode DetectImageMode()
    {
        var overrideValue = Environment.GetEnvironmentVariable("KGTEST_IMAGE");
        if (TryParseMode(overrideValue, out var forced) && forced != TerminalImageMode.Auto)
        {
            return forced;
        }

        var term = Env("TERM").ToLowerInvariant();
        var program = Env("TERM_PROGRAM").ToLowerInvariant();
        var lcTerminal = Env("LC_TERMINAL").ToLowerInvariant();

        // tmux / screen 需要额外的透传配置，默认退回字符块。
        if (!string.IsNullOrEmpty(Env("TMUX")) || term.StartsWith("screen", StringComparison.Ordinal) ||
            term.StartsWith("tmux", StringComparison.Ordinal))
        {
            return TerminalImageMode.Blocks;
        }

        if (!string.IsNullOrEmpty(Env("KITTY_WINDOW_ID")) || term.Contains("kitty") || program == "ghostty" ||
            term.Contains("ghostty"))
        {
            return TerminalImageMode.Kitty;
        }

        if (program is "iterm.app" or "wezterm" || lcTerminal is "iterm2" or "wezterm" ||
            !string.IsNullOrEmpty(Env("WEZTERM_EXECUTABLE")))
        {
            return TerminalImageMode.ITerm;
        }

        if (term.Contains("foot") || term.Contains("mlterm") || term.Contains("sixel"))
        {
            return TerminalImageMode.Sixel;
        }

        return TerminalImageMode.Blocks;
    }

    /// <summary>
    ///     推断终端可用的颜色深度。真彩色不可用时降级到 xterm-256 色；Linux 虚拟终端等只有 16 色的环境
    ///     必须降到 ANSI 16 色，否则 38;5;n / 48;2;r;g;b 会被终端直接丢弃，选中行的背景色随之消失。
    /// </summary>
    public static TerminalColorDepth DetectColorDepth()
    {
        var forced = Env("KGTEST_COLOR").Trim().ToLowerInvariant();
        switch (forced)
        {
            case "16":
            case "ansi":
            case "ansi16":
                return TerminalColorDepth.Ansi16;
            case "256":
            case "ansi256":
                return TerminalColorDepth.Ansi256;
            case "truecolor":
            case "24bit":
                return TerminalColorDepth.TrueColor;
        }

        var colorTerm = Env("COLORTERM").Trim().ToLowerInvariant();
        if (colorTerm is "truecolor" or "24bit")
        {
            return TerminalColorDepth.TrueColor;
        }

        var program = Env("TERM_PROGRAM").ToLowerInvariant().Trim();
        if (OperatingSystem.IsWindows() || !string.IsNullOrEmpty(Env("WT_SESSION")) ||
            program is "iterm.app" or "wezterm" or "ghostty" or "vscode")
        {
            return TerminalColorDepth.TrueColor;
        }

        var term = Env("TERM").Trim().ToLowerInvariant();
        if (program == "apple_terminal")
        {
            return TerminalColorDepth.Ansi256;
        }

        if (term.Contains("kitty") || term.Contains("direct") || term.Contains("truecolor"))
        {
            return TerminalColorDepth.TrueColor;
        }

        if (term.Contains("256"))
        {
            return TerminalColorDepth.Ansi256;
        }

        // 内核虚拟终端与只支持 8/16 色的老式终端/多路复用器。
        if (term.StartsWith("linux", StringComparison.Ordinal) || term.StartsWith("console", StringComparison.Ordinal) ||
            term.StartsWith("vt", StringComparison.Ordinal) || term.StartsWith("screen", StringComparison.Ordinal) ||
            term.StartsWith("tmux", StringComparison.Ordinal) || term == "dumb")
        {
            return TerminalColorDepth.Ansi16;
        }

        return TerminalColorDepth.Ansi256;
    }

    /// <summary>
    ///     终端字符格的像素尺寸，用于按终端真实比例缩放图片；可通过 KGTEST_CELL_SIZE=10x20 指定。
    ///     Unix 终端会用 ioctl(TIOCGWINSZ) 读取窗口像素尺寸后除以行列数，读取失败时回退到常见的 1:2 字符格。
    /// </summary>
    public static (int Width, int Height) GetCellPixelSize()
    {
        var value = Env("KGTEST_CELL_SIZE");
        var parts = value.Split('x', 'X');
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w > 0 &&
            h > 0)
        {
            return (w, h);
        }

        return ProbeCellPixelSize() ?? (10, 20);
    }

    private static (int Width, int Height)? ProbeCellPixelSize()
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var size = default(WinSize);
            var mac = OperatingSystem.IsMacOS();
            var request = mac ? MacTiocgWinSz : LinuxTiocgWinSz;
            // Darwin ARM64 puts variadic arguments on the stack, unlike Linux ARM64
            // and macOS x64. Fill x2-x7 so the WinSize pointer occupies that stack slot.
            var result = mac
                ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? MacArm64Ioctl(1, request, 0, 0, 0, 0, 0, 0, ref size)
                    : MacIoctl(1, request, ref size)
                : LinuxIoctl(1, request, ref size);
            if (result != 0 || size.Rows == 0 || size.Cols == 0 || size.XPixel == 0 || size.YPixel == 0)
            {
                return null;
            }

            var width = (int)Math.Round(size.XPixel / (double)size.Cols);
            var height = (int)Math.Round(size.YPixel / (double)size.Rows);
            return width > 0 && height > 0 ? (width, height) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public static bool TryParseMode(string? value, out TerminalImageMode mode)
    {
        mode = TerminalImageMode.Auto;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "auto":
                mode = TerminalImageMode.Auto;
                return true;
            case "kitty":
                mode = TerminalImageMode.Kitty;
                return true;
            case "iterm":
            case "iterm2":
                mode = TerminalImageMode.ITerm;
                return true;
            case "sixel":
                mode = TerminalImageMode.Sixel;
                return true;
            case "blocks":
            case "block":
            case "halfblock":
                mode = TerminalImageMode.Blocks;
                return true;
            case "off":
            case "none":
                mode = TerminalImageMode.Off;
                return true;
            default:
                return false;
        }
    }

    public static string DisplayName(TerminalImageMode mode)
    {
        return mode switch
        {
            TerminalImageMode.Auto => "自动",
            TerminalImageMode.Kitty => "Kitty 图形协议",
            TerminalImageMode.ITerm => "iTerm2 内联图片",
            TerminalImageMode.Sixel => "Sixel",
            TerminalImageMode.Blocks => "半块字符",
            TerminalImageMode.Off => "关闭",
            _ => mode.ToString()
        };
    }

    private const nuint LinuxTiocgWinSz = 0x5413;
    private const nuint MacTiocgWinSz = 0x40087468;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort Rows;
        public ushort Cols;
        public ushort XPixel;
        public ushort YPixel;
    }

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int LinuxIoctl(int fd, nuint request, ref WinSize size);

    [DllImport("libSystem.dylib", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int MacIoctl(int fd, nuint request, ref WinSize size);

    // Only call on macOS ARM64. The six padding arguments consume the unused
    // integer argument registers; ioctl's first variadic argument starts at SP.
    [DllImport("libSystem.dylib", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int MacArm64Ioctl(int fd, nuint request,
        nuint x2, nuint x3, nuint x4, nuint x5, nuint x6, nuint x7, ref WinSize size);

    private static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";
}
