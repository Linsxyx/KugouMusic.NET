using System.Text;
using KaMusic.Cli.Graphics;
using KaMusic.Cli.Tui;
using Xunit;

namespace KaMusic.Tests;

// 低色深终端的颜色输出：Linux 虚拟终端（TERM=linux）只有 16 色，写入 256 色或 24 位真彩 SGR 会被
// 终端直接丢弃，选中行的背景色随之消失。这里锁定探测结果与降级后的编码。
public sealed class TerminalColorTests
{
    private static readonly string[] EnvNames =
        ["KGTEST_COLOR", "TERM", "COLORTERM", "TERM_PROGRAM", "WT_SESSION"];

    private static string Sgr(CellStyle style, TerminalColorDepth depth)
    {
        var builder = new StringBuilder();
        Ansi.AppendSgr(builder, style, depth);
        return builder.ToString();
    }

    private static T WithEnvironment<T>(Func<T> body)
    {
        var originals = EnvNames.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            return body();
        }
        finally
        {
            for (var i = 0; i < EnvNames.Length; i++)
            {
                Environment.SetEnvironmentVariable(EnvNames[i], originals[i]);
            }
        }
    }

    [Fact]
    public void Ansi16_EmitsBasicColorCodesOnly()
    {
        var theme = new Theme();
        var sgr = Sgr(new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold), TerminalColorDepth.Ansi16);

        Assert.Equal("\u001b[0;1;97;44m", sgr);
        Assert.False(sgr.Contains(";5;", StringComparison.Ordinal));
        Assert.False(sgr.Contains(";2;", StringComparison.Ordinal));
    }

    [Fact]
    public void Ansi16_CoversWholeThemeWithoutExtendedSequences()
    {
        var theme = new Theme();
        CellStyle[] styles =
        [
            theme.TextStyle, theme.MutedStyle, theme.FaintStyle, theme.BorderStyle, theme.AccentStyle,
            new(theme.Text, theme.SelectionBg, TextAttr.Bold),
            new(theme.Text, theme.SelectionDimBg),
            new(theme.Muted, theme.PanelBg),
            new(theme.Accent, theme.InputFocusBg),
            new(theme.Text, theme.InputBg, TextAttr.Bold)
        ];

        foreach (var style in styles)
        {
            var sgr = Sgr(style, TerminalColorDepth.Ansi16);
            Assert.False(sgr.Contains(";5;", StringComparison.Ordinal), sgr);
            Assert.False(sgr.Contains(";2;", StringComparison.Ordinal), sgr);
        }
    }

    [Fact]
    public void Ansi256_UsesIndexedSequences()
    {
        var theme = new Theme();
        var sgr = Sgr(new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold), TerminalColorDepth.Ansi256);

        Assert.Contains(";48;5;", sgr, StringComparison.Ordinal);
        Assert.Contains(";38;5;", sgr, StringComparison.Ordinal);
        Assert.DoesNotContain(";2;", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void TrueColor_EmitsChannelValues()
    {
        var theme = new Theme();
        var sgr = Sgr(new CellStyle(theme.Text, theme.SelectionBg, TextAttr.Bold), TerminalColorDepth.TrueColor);

        Assert.Contains(";48;2;30;58;95", sgr, StringComparison.Ordinal);
        Assert.Contains(";38;2;230;232;235", sgr, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionBackgroundStaysVisibleInAnsi16()
    {
        var theme = new Theme();
        var selection = Ansi.ToAnsi16(theme.SelectionBg);
        var selectionDim = Ansi.ToAnsi16(theme.SelectionDimBg);

        // 选中行必须和“无背景”（终端默认背景，量化到黑色）以及面板背景区分开。
        Assert.NotEqual(selection, Ansi.ToAnsi16(theme.PanelBg));
        Assert.NotEqual(selection, Ansi.ToAnsi16(theme.InputBg));
        Assert.NotEqual(selection, Ansi.ToAnsi16(Rgb.FromHex(0x000000)));
        Assert.NotEqual(selection, selectionDim);
    }

    [Theory]
    [InlineData(38, 0, 30)]
    [InlineData(38, 4, 34)]
    [InlineData(38, 8, 90)]
    [InlineData(48, 0, 40)]
    [InlineData(48, 4, 44)]
    [InlineData(48, 12, 104)]
    public void ToAnsi16Code_MapsForegroundAndBackgroundRanges(int target, int index, int expected)
    {
        Assert.Equal(expected, Ansi.ToAnsi16Code(target, index));
    }

    [Theory]
    [InlineData(0x00, 0x00, 0x00, 16)]
    [InlineData(0xFF, 0xFF, 0xFF, 231)]
    [InlineData(0x80, 0x80, 0x80, 244)]
    public void To256_MatchesXtermCubeAndGrayscale(byte r, byte g, byte b, int expected)
    {
        Assert.Equal(expected, Ansi.To256(new Rgb(r, g, b)));
    }

    [Theory]
    [InlineData("16", "Ansi16")]
    [InlineData("ansi", "Ansi16")]
    [InlineData("ansi256", "Ansi256")]
    [InlineData("24bit", "TrueColor")]
    public void DetectColorDepth_HonorsExplicitOverride(string value, string expected)
    {
        var actual = WithEnvironment(() =>
        {
            Environment.SetEnvironmentVariable("KGTEST_COLOR", value);
            return TerminalCapabilities.DetectColorDepth();
        });

        Assert.Equal(expected, actual.ToString());
    }

    [Theory]
    [InlineData("linux", "Ansi16")]
    [InlineData("console", "Ansi16")]
    [InlineData("dumb", "Ansi16")]
    [InlineData("xterm-256color", "Ansi256")]
    [InlineData("xterm-kitty", "TrueColor")]
    public void DetectColorDepth_FallsBackToTerm(string term, string expected)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var actual = WithEnvironment(() =>
        {
            Environment.SetEnvironmentVariable("KGTEST_COLOR", null);
            Environment.SetEnvironmentVariable("COLORTERM", null);
            Environment.SetEnvironmentVariable("TERM_PROGRAM", null);
            Environment.SetEnvironmentVariable("WT_SESSION", null);
            Environment.SetEnvironmentVariable("TERM", term);
            return TerminalCapabilities.DetectColorDepth();
        });

        Assert.Equal(expected, actual.ToString());
    }
}
