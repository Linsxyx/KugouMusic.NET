using System.Text;
using KaMusic.Cli.Graphics;

namespace KaMusic.Cli.Tui;

// 颜色量化与 SGR 序列生成。Linux 虚拟终端（Ctrl+Alt+F1..F6，TERM=linux）只支持 ANSI 16 色：
// 直接把 RGB 写成 24 位或 256 色序列会被终端丢弃（48;5;n 里的 5 甚至会被当成“闪烁”），
// 选中行的背景色就会完全消失，导致设置页里无法分辨当前选中的条目。
// 因此这里按探测到的色深挑选终端真正支持的序列。

internal static class Ansi
{
    private static readonly int[] CubeLevels = [0, 95, 135, 175, 215, 255];

    // xterm 标准 16 色调色板：0-7 常规色，8-15 高亮色。
    private static readonly Rgb[] Palette16 =
    [
        new(0x00, 0x00, 0x00), new(0x80, 0x00, 0x00), new(0x00, 0x80, 0x00), new(0x80, 0x80, 0x00),
        new(0x00, 0x00, 0x80), new(0x80, 0x00, 0x80), new(0x00, 0x80, 0x80), new(0xC0, 0xC0, 0xC0),
        new(0x80, 0x80, 0x80), new(0xFF, 0x00, 0x00), new(0x00, 0xFF, 0x00), new(0xFF, 0xFF, 0x00),
        new(0x00, 0x00, 0xFF), new(0xFF, 0x00, 0xFF), new(0x00, 0xFF, 0xFF), new(0xFF, 0xFF, 0xFF)
    ];

    /// <summary>
    ///    把单元格样式编码成完整的 SGR 序列（含前导 ESC[0），供差量渲染器直接写入。
    /// </summary>
    public static void AppendSgr(StringBuilder buffer, CellStyle style, TerminalColorDepth depth)
    {
        buffer.Append("\u001b[0");
        if (style.Attr.HasFlag(TextAttr.Bold))
        {
            buffer.Append(";1");
        }

        if (style.Attr.HasFlag(TextAttr.Dim))
        {
            buffer.Append(";2");
        }

        if (style.Attr.HasFlag(TextAttr.Italic))
        {
            buffer.Append(";3");
        }

        if (style.Attr.HasFlag(TextAttr.Underline))
        {
            buffer.Append(";4");
        }

        if (style.Fg is { } fg)
        {
            AppendColor(buffer, 38, fg, depth);
        }

        if (style.Bg is { } bg)
        {
            AppendColor(buffer, 48, bg, depth);
        }

        buffer.Append('m');
    }

    private static void AppendColor(StringBuilder buffer, int target, Rgb color, TerminalColorDepth depth)
    {
        switch (depth)
        {
            case TerminalColorDepth.TrueColor:
                buffer.Append(';').Append(target).Append(";2;").Append(color.R).Append(';').Append(color.G)
                    .Append(';').Append(color.B);
                break;
            case TerminalColorDepth.Ansi256:
                buffer.Append(';').Append(target).Append(";5;").Append(To256(color));
                break;
            default:
                // 16 色终端只认 30-37 / 90-97（前景）与 40-47 / 100-107（背景）。
                buffer.Append(';').Append(ToAnsi16Code(target, ToAnsi16(color)));
                break;
        }
    }

    /// <summary>
    ///    把 16 色索引换算成 SGR 码：target 为 38 表示前景，48 表示背景。
    /// </summary>
    public static int ToAnsi16Code(int target, int index) =>
        target == 38
            ? index < 8 ? 30 + index : 90 + index - 8
            : index < 8 ? 40 + index : 100 + index - 8;

    /// <summary>
    ///    在 xterm 标准 16 色中查找最接近的颜色索引，距离使用 redmean 近似感知度量。
    /// </summary>
    public static int ToAnsi16(Rgb color)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < Palette16.Length; i++)
        {
            var candidate = Palette16[i];
            var rMean = (color.R + candidate.R) / 2;
            var dr = color.R - candidate.R;
            var dg = color.G - candidate.G;
            var db = color.B - candidate.B;
            var distance = ((512 + rMean) * dr * dr >> 8) + 4 * dg * dg + ((767 - rMean) * db * db >> 8);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    ///    将 RGB 映射到 xterm 256 色（6×6×6 色立方或灰阶中更接近者）。
    /// </summary>
    public static int To256(Rgb color)
    {
        static int CubeIndex(int v) => v < 48 ? 0 : v < 115 ? 1 : Math.Min(5, (v - 35) / 40);

        var ri = CubeIndex(color.R);
        var gi = CubeIndex(color.G);
        var bi = CubeIndex(color.B);
        var cubeR = CubeLevels[ri];
        var cubeG = CubeLevels[gi];
        var cubeB = CubeLevels[bi];

        var average = (color.R + color.G + color.B) / 3;
        var grayIndex = average > 238 ? 23 : Math.Max(0, (average - 3) / 10);
        var grayValue = 8 + grayIndex * 10;

        var cubeDistance = Sq(color.R - cubeR) + Sq(color.G - cubeG) + Sq(color.B - cubeB);
        var grayDistance = Sq(color.R - grayValue) + Sq(color.G - grayValue) + Sq(color.B - grayValue);
        return grayDistance < cubeDistance ? 232 + grayIndex : 16 + ri * 36 + gi * 6 + bi;
    }

    private static int Sq(int v) => v * v;
}
