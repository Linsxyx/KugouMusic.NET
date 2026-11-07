namespace KaMusic.Cli.Graphics;

// 终端图形能力：图片协议与颜色深度的探测。可通过环境变量 KGTEST_IMAGE / KGTEST_COLOR 覆盖。

internal enum TerminalImageMode
{
    Auto,
    Kitty,
    ITerm,
    Sixel,
    Blocks,
    Off
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
    ///     是否支持 24 位真彩色；不支持时 UI 会降级到 256 色。
    /// </summary>
    public static bool DetectTrueColor()
    {
        var forced = Env("KGTEST_COLOR").ToLowerInvariant();
        if (forced is "256" or "ansi256")
        {
            return false;
        }

        if (forced is "truecolor" or "24bit")
        {
            return true;
        }

        var colorTerm = Env("COLORTERM").ToLowerInvariant();
        if (colorTerm is "truecolor" or "24bit")
        {
            return true;
        }

        var program = Env("TERM_PROGRAM").ToLowerInvariant();
        if (program == "apple_terminal")
        {
            return false;
        }

        if (OperatingSystem.IsWindows() || !string.IsNullOrEmpty(Env("WT_SESSION")))
        {
            return true;
        }

        var term = Env("TERM").ToLowerInvariant();
        return program is "iterm.app" or "wezterm" or "ghostty" or "vscode" || term.Contains("kitty") ||
               term.Contains("direct") || term.Contains("truecolor");
    }

    /// <summary>
    ///     终端字符格的像素尺寸，只用于 Sixel 输出；可通过 KGTEST_CELL_SIZE=10x20 指定。
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

        return (10, 20);
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

    private static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "";
}
