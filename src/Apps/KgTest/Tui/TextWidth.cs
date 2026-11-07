using System.Text;

namespace KgTest.Tui;

// 终端显示宽度计算：CJK / 全角 / emoji 占 2 列，组合字符占 0 列。

internal static class TextWidth
{
    private static readonly (int Start, int End)[] WideRanges =
    [
        (0x1100, 0x115F), (0x231A, 0x231B), (0x2329, 0x232A), (0x23E9, 0x23EC), (0x23F0, 0x23F0),
        (0x23F3, 0x23F3), (0x25FD, 0x25FE), (0x2614, 0x2615), (0x2648, 0x2653), (0x267F, 0x267F),
        (0x2693, 0x2693), (0x26A1, 0x26A1), (0x26AA, 0x26AB), (0x26BD, 0x26BE), (0x26C4, 0x26C5),
        (0x26CE, 0x26CE), (0x26D4, 0x26D4), (0x26EA, 0x26EA), (0x26F2, 0x26F3), (0x26F5, 0x26F5),
        (0x26FA, 0x26FA), (0x26FD, 0x26FD), (0x2705, 0x2705), (0x270A, 0x270B), (0x2728, 0x2728),
        (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797),
        (0x27B0, 0x27B0), (0x27BF, 0x27BF), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55),
        (0x2E80, 0x303E), (0x3041, 0x33FF), (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xA000, 0xA4CF),
        (0xA960, 0xA97F), (0xAC00, 0xD7A3), (0xF900, 0xFAFF), (0xFE10, 0xFE19), (0xFE30, 0xFE6F),
        (0xFF00, 0xFF60), (0xFFE0, 0xFFE6), (0x16FE0, 0x16FE4), (0x17000, 0x18CFF), (0x1B000, 0x1B2FF),
        (0x1F004, 0x1F004), (0x1F0CF, 0x1F0CF), (0x1F18E, 0x1F18E), (0x1F191, 0x1F19A), (0x1F200, 0x1F251),
        (0x1F300, 0x1F64F), (0x1F680, 0x1F6FF), (0x1F7E0, 0x1F7EB), (0x1F90C, 0x1F9FF), (0x1FA70, 0x1FAFF),
        (0x20000, 0x2FFFD), (0x30000, 0x3FFFD)
    ];

    public static int Of(Rune rune) => Of(rune.Value);

    public static int Of(int cp)
    {
        if (cp == 0 || cp < 0x20 || cp is >= 0x7F and < 0xA0)
        {
            return 0;
        }

        if (cp < 0x1100)
        {
            return cp is >= 0x0300 and <= 0x036F ? 0 : 1;
        }

        if (cp is >= 0x200B and <= 0x200F or >= 0x20D0 and <= 0x20FF or >= 0xFE00 and <= 0xFE0F or 0x2060)
        {
            return 0;
        }

        var lo = 0;
        var hi = WideRanges.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var (start, end) = WideRanges[mid];
            if (cp < start)
            {
                hi = mid - 1;
            }
            else if (cp > end)
            {
                lo = mid + 1;
            }
            else
            {
                return 2;
            }
        }

        return 1;
    }

    public static int Of(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += Of(rune);
        }

        return width;
    }

    /// <summary>
    ///     按显示宽度截断文本，超出时以省略号结尾。
    /// </summary>
    public static string Truncate(string? text, int maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            return "";
        }

        if (Of(text) <= maxWidth)
        {
            return text;
        }

        var builder = new StringBuilder();
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var w = Of(rune);
            if (width + w > maxWidth - 1)
            {
                break;
            }

            builder.Append(rune.ToString());
            width += w;
        }

        builder.Append('…');
        return builder.ToString();
    }

    public static string PadRight(string? text, int width)
    {
        var value = Truncate(text, width);
        var pad = width - Of(value);
        return pad > 0 ? value + new string(' ', pad) : value;
    }
}
