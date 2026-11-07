using SkiaSharp;

namespace KaMusic.Cli.Graphics;

// 从二维码位图中还原模块矩阵，供不支持图片协议的终端用半块字符精确绘制可扫码的二维码。

internal static class QrMatrixExtractor
{
    /// <summary>
    ///     通过定位左上角定位图案推算模块尺寸，然后对每个模块中心采样。
    /// </summary>
    public static bool[,]? Extract(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        if (width < 21 || height < 21)
        {
            return null;
        }

        var dark = new bool[width, height];
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                var lum = (c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000;
                var isDark = c.Alpha > 128 && lum < 128;
                dark[x, y] = isDark;
                if (!isDark)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < 0)
        {
            return null;
        }

        // 定位图案最上一行连续 7 个深色模块。
        var finderRowY = minY + 1 < height ? minY + 1 : minY;
        var run = 0;
        for (var x = minX; x <= maxX && dark[x, finderRowY]; x++)
        {
            run++;
        }

        if (run < 7)
        {
            return null;
        }

        var moduleSize = run / 7.0;
        var spanX = maxX - minX + 1;
        var spanY = maxY - minY + 1;
        var estimated = (int)Math.Round(spanX / moduleSize);
        var size = SnapToVersionSize(estimated);
        if (size < 21 || size > 177)
        {
            return null;
        }

        var stepX = spanX / (double)size;
        var stepY = spanY / (double)size;
        var modules = new bool[size, size];
        for (var my = 0; my < size; my++)
        {
            for (var mx = 0; mx < size; mx++)
            {
                var px = Math.Clamp((int)(minX + (mx + 0.5) * stepX), 0, width - 1);
                var py = Math.Clamp((int)(minY + (my + 0.5) * stepY), 0, height - 1);
                modules[mx, my] = dark[px, py];
            }
        }

        return modules;
    }

    private static int SnapToVersionSize(int estimated)
    {
        // 合法尺寸为 21 + 4k。
        var version = (int)Math.Round((estimated - 17) / 4.0);
        return 17 + Math.Clamp(version, 1, 40) * 4;
    }
}
