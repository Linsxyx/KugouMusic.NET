using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using KaMusic.Cli.Tui;

namespace KaMusic.Cli.Graphics;

// 终端图形能力：图片协议、颜色深度、背景明暗主题与字符格像素尺寸的探测。可通过环境变量
// KGTEST_IMAGE / KGTEST_COLOR / KGTEST_THEME / KGTEST_CELL_SIZE 覆盖。

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

internal enum TerminalThemeMode
{
    Auto,
    Dark,
    Light
}

internal enum TerminalThemeKind
{
    Dark,
    Light
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
    ///     探测终端背景是浅色还是深色：优先遵循 KGTEST_THEME，其次在交互式终端通过 OSC 11 查询真实背景色，
    ///     最后回退到 COLORFGBG 环境变量与深色默认值。
    /// </summary>
    public static TerminalThemeKind DetectThemeKind(bool probeOsc11 = true, Func<string?>? osc11Query = null)
    {
        if (TryParseThemeMode(Env("KGTEST_THEME"), out var forced) && forced != TerminalThemeMode.Auto)
        {
            return forced == TerminalThemeMode.Light ? TerminalThemeKind.Light : TerminalThemeKind.Dark;
        }

        if (probeOsc11)
        {
            var response = osc11Query != null ? osc11Query() : QueryOsc11Response();
            if (!string.IsNullOrEmpty(response) && TryParseOsc11Background(response, out var bg))
            {
                return bg.Luminance > 0.5f ? TerminalThemeKind.Light : TerminalThemeKind.Dark;
            }
        }

        if (TryParseColorFgBg(Env("COLORFGBG"), out var fgBgKind))
        {
            return fgBgKind;
        }

        return TerminalThemeKind.Dark;
    }

    /// <summary>
    ///     解析 COLORFGBG（常见格式为 "fg;bg" 或 "fg;extra;bg"），判断终端背景明暗。
    /// </summary>
    public static bool TryParseColorFgBg(string? value, out TerminalThemeKind kind)
    {
        kind = TerminalThemeKind.Dark;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bg))
        {
            return false;
        }

        if (bg is 7 or 10 or 11 or 14 or 15)
        {
            kind = TerminalThemeKind.Light;
            return true;
        }

        if (bg is >= 0 and <= 15)
        {
            kind = TerminalThemeKind.Dark;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     解析终端 OSC 11 背景色应答（支持 rgb:R/G/B、rgba:R/G/B/A 的 1~4 位十六进制分量及 #RRGGBB 格式）。
    /// </summary>
    public static bool TryParseOsc11Background(string? response, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        var rgbIdx = response.IndexOf("rgba:", StringComparison.OrdinalIgnoreCase);
        var prefixLen = 5;
        if (rgbIdx < 0)
        {
            rgbIdx = response.IndexOf("rgb:", StringComparison.OrdinalIgnoreCase);
            prefixLen = 4;
        }

        if (rgbIdx >= 0)
        {
            var payload = response[(rgbIdx + prefixLen)..];
            var end = payload.IndexOfAny(['\u0007', '\u001b', '\\', '\r', '\n']);
            if (end >= 0)
            {
                payload = payload[..end];
            }

            var channels = payload.Split('/');
            if (channels.Length >= 3 &&
                TryParseHexChannel(channels[0], out var r) &&
                TryParseHexChannel(channels[1], out var g) &&
                TryParseHexChannel(channels[2], out var b))
            {
                color = new Rgb(r, g, b);
                return true;
            }
        }

        var hashIdx = response.IndexOf('#');
        if (hashIdx >= 0 && hashIdx + 7 <= response.Length)
        {
            var hex = response.Substring(hashIdx + 1, 6);
            if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            {
                color = Rgb.FromHex(packed);
                return true;
            }
        }

        return false;
    }

    private static bool TryParseHexChannel(string hex, out byte value)
    {
        value = 0;
        var trimmed = hex.Trim();
        if (trimmed.Length is < 1 or > 4 ||
            !int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        var max = (1 << (trimmed.Length * 4)) - 1;
        value = (byte)Math.Clamp((int)Math.Round(parsed * 255.0 / max), 0, 255);
        return true;
    }

    /// <summary>
    ///     向交互式终端发送 OSC 11 查询并限时读取背景色应答；不支持的终端会在超时后安全返回 null。
    /// </summary>
    public static string? QueryOsc11Response(int timeoutMs = 120)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return null;
        }

        var term = Env("TERM").Trim().ToLowerInvariant();
        if (term is "dumb" or "linux" or "console" ||
            term.StartsWith("vt", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            // 先触发一次 KeyAvailable，让 Unix ConsolePal 在输出查询前关闭终端回显，避免应答被打印出来。
            _ = Console.KeyAvailable;
            Console.Out.Write("\u001b]11;?\u0007");
            Console.Out.Flush();

            return OperatingSystem.IsWindows()
                ? ReadOsc11ViaConsoleKeys(timeoutMs)
                : ReadOsc11ViaUnixPoll(timeoutMs);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadOsc11ViaUnixPoll(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        var bytes = new List<byte>(48);
        var buf = new byte[1];
        var pfd = new PollFd { Fd = 0, Events = PollIn };
        var inOsc = false;
        var prevEsc = false;
        var mac = OperatingSystem.IsMacOS();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var remaining = (int)Math.Max(1, timeoutMs - sw.ElapsedMilliseconds);
            var ready = mac ? MacPoll(ref pfd, 1, Math.Min(remaining, 20)) : LinuxPoll(ref pfd, 1, Math.Min(remaining, 20));
            if (ready <= 0 || (pfd.Revents & PollIn) == 0)
            {
                continue;
            }

            var read = mac ? MacRead(0, buf, 1) : LinuxRead(0, buf, 1);
            if (read <= 0)
            {
                break;
            }

            var b = buf[0];
            if (!inOsc)
            {
                if (prevEsc && b == (byte)']')
                {
                    inOsc = true;
                    prevEsc = false;
                    bytes.Add(0x1b);
                    bytes.Add((byte)']');
                }
                else
                {
                    prevEsc = b == 0x1b;
                }

                continue;
            }

            bytes.Add(b);
            if (b == 0x07 || (prevEsc && b == (byte)'\\'))
            {
                return Encoding.UTF8.GetString(bytes.ToArray());
            }

            prevEsc = b == 0x1b;
            if (bytes.Count > 128)
            {
                break;
            }
        }

        return null;
    }

    private static string? ReadOsc11ViaConsoleKeys(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        var sb = new StringBuilder(48);
        var inOsc = false;
        var prevEsc = false;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (!Console.KeyAvailable)
            {
                Thread.Sleep(2);
                continue;
            }

            var key = Console.ReadKey(true);
            var ch = key.Key == ConsoleKey.Escape ? '\u001b' : key.KeyChar;
            var hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;

            if (!inOsc)
            {
                if ((prevEsc || hasAlt) && ch == ']')
                {
                    inOsc = true;
                    prevEsc = false;
                    sb.Append("\u001b]");
                }
                else
                {
                    prevEsc = ch == '\u001b';
                }

                continue;
            }

            sb.Append(ch);
            // 某些情况下 .NET Console.ReadKey 会吞掉 ST (\u001b\\) 的前导 ESC，因此颜色负载内出现 '\' 即可结束。
            if (ch is '\u0007' or '\\')
            {
                return sb.ToString();
            }

            if (sb.Length > 128)
            {
                break;
            }
        }

        return null;
    }

    public static bool TryParseThemeMode(string? value, out TerminalThemeMode mode)
    {
        mode = TerminalThemeMode.Auto;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "auto":
                mode = TerminalThemeMode.Auto;
                return true;
            case "dark":
                mode = TerminalThemeMode.Dark;
                return true;
            case "light":
                mode = TerminalThemeMode.Light;
                return true;
            default:
                return false;
        }
    }

    public static string DisplayName(TerminalThemeMode mode) => mode switch
    {
        TerminalThemeMode.Auto => "自动",
        TerminalThemeMode.Dark => "深色",
        TerminalThemeMode.Light => "浅色",
        _ => mode.ToString()
    };

    public static string DisplayName(TerminalThemeKind kind) => kind switch
    {
        TerminalThemeKind.Light => "浅色",
        _ => "深色"
    };

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
    private const short PollIn = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort Rows;
        public ushort Cols;
        public ushort XPixel;
        public ushort YPixel;
    }

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int LinuxPoll(ref PollFd fds, uint nfds, int timeout);

    [DllImport("libSystem.dylib", EntryPoint = "poll", SetLastError = true)]
    private static extern int MacPoll(ref PollFd fds, uint nfds, int timeout);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint LinuxRead(int fd, byte[] buf, nuint count);

    [DllImport("libSystem.dylib", EntryPoint = "read", SetLastError = true)]
    private static extern nint MacRead(int fd, byte[] buf, nuint count);

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
