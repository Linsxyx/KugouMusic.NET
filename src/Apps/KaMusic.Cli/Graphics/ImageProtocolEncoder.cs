using System.Text;
using SkiaSharp;

namespace KaMusic.Cli.Graphics;

// 三种终端图片协议的编码器：Kitty graphics、iTerm2 inline image、Sixel。

internal static class ImageProtocolEncoder
{
    private const int KittyChunkSize = 4096;

    /// <summary>
    ///     Kitty：以 PNG 传输并放置到 cols×rows 个字符格内，q=2 关闭终端回执，C=1 不移动光标。
    /// </summary>
    public static void AppendKitty(StringBuilder output, TerminalImage image, int id, int cols, int rows,
        int pixelWidth, int pixelHeight)
    {
        var png = image.Encode(pixelWidth, pixelHeight, SKEncodedImageFormat.Png);
        var base64 = Convert.ToBase64String(png);
        for (var offset = 0; offset < base64.Length; offset += KittyChunkSize)
        {
            var length = Math.Min(KittyChunkSize, base64.Length - offset);
            var more = offset + length < base64.Length ? 1 : 0;
            output.Append("\u001b_G");
            if (offset == 0)
            {
                output.Append($"a=T,f=100,t=d,i={id},c={cols},r={rows},C=1,q=2,");
            }

            output.Append("m=").Append(more).Append(';');
            output.Append(base64, offset, length);
            output.Append("\u001b\\");
        }
    }

    public static void AppendKittyDelete(StringBuilder output, int id)
    {
        output.Append($"\u001b_Ga=d,d=I,i={id},q=2\u001b\\");
    }

    public static void AppendKittyDeleteAll(StringBuilder output)
    {
        output.Append("\u001b_Ga=d,d=A,q=2\u001b\\");
    }

    /// <summary>
    ///     iTerm2 / WezTerm：OSC 1337 内联图片，尺寸以字符格为单位。
    /// </summary>
    public static void AppendITerm(StringBuilder output, TerminalImage image, int cols, int rows, int pixelWidth,
        int pixelHeight)
    {
        var format = image.Crisp ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        var bytes = image.Encode(pixelWidth, pixelHeight, format);
        output.Append("\u001b]1337;File=inline=1;size=").Append(bytes.Length)
            .Append(";width=").Append(cols)
            .Append(";height=").Append(rows)
            .Append(";preserveAspectRatio=0;doNotMoveCursor=1:");
        output.Append(Convert.ToBase64String(bytes));
        output.Append('\a');
    }

    /// <summary>
    ///     Sixel：量化到 6×6×6 调色板后按 6 行一组输出，带行程压缩。
    /// </summary>
    public static void AppendSixel(StringBuilder output, TerminalImage image, int pixelWidth, int pixelHeight)
    {
        var pixels = image.GetPixels(pixelWidth, pixelHeight);
        var indices = new byte[pixels.Length];
        var used = new bool[216];
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            var index = (byte)(Quantize(p.R) * 36 + Quantize(p.G) * 6 + Quantize(p.B));
            indices[i] = index;
            used[index] = true;
        }

        output.Append("\u001bP0;0;0q\"1;1;").Append(pixelWidth).Append(';').Append(pixelHeight);
        for (var i = 0; i < used.Length; i++)
        {
            if (!used[i])
            {
                continue;
            }

            var r = i / 36 * 20;
            var g = i / 6 % 6 * 20;
            var b = i % 6 * 20;
            output.Append('#').Append(i).Append(";2;").Append(r).Append(';').Append(g).Append(';').Append(b);
        }

        var bandColors = new bool[216];
        for (var bandTop = 0; bandTop < pixelHeight; bandTop += 6)
        {
            Array.Clear(bandColors);
            var bandHeight = Math.Min(6, pixelHeight - bandTop);
            for (var y = bandTop; y < bandTop + bandHeight; y++)
            {
                for (var x = 0; x < pixelWidth; x++)
                {
                    bandColors[indices[y * pixelWidth + x]] = true;
                }
            }

            var firstColor = true;
            for (var color = 0; color < 216; color++)
            {
                if (!bandColors[color])
                {
                    continue;
                }

                if (!firstColor)
                {
                    output.Append('$');
                }

                firstColor = false;
                output.Append('#').Append(color);
                var runChar = '\0';
                var runLength = 0;
                for (var x = 0; x < pixelWidth; x++)
                {
                    var bits = 0;
                    for (var dy = 0; dy < bandHeight; dy++)
                    {
                        if (indices[(bandTop + dy) * pixelWidth + x] == color)
                        {
                            bits |= 1 << dy;
                        }
                    }

                    var ch = (char)(63 + bits);
                    if (ch == runChar)
                    {
                        runLength++;
                        continue;
                    }

                    FlushRun(output, runChar, runLength);
                    runChar = ch;
                    runLength = 1;
                }

                FlushRun(output, runChar, runLength);
            }

            output.Append('-');
        }

        output.Append("\u001b\\");
    }

    private static int Quantize(byte value) => (value * 5 + 127) / 255;

    private static void FlushRun(StringBuilder output, char ch, int length)
    {
        if (length <= 0)
        {
            return;
        }

        if (length > 3)
        {
            output.Append('!').Append(length).Append(ch);
        }
        else
        {
            output.Append(ch, length);
        }
    }
}
