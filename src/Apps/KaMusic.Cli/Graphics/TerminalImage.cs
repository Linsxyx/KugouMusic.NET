using KaMusic.Cli.Tui;
using SkiaSharp;

namespace KaMusic.Cli.Graphics;

// 已解码的终端图片：持有原始位图，并缓存按尺寸缩放后的像素与编码结果。

internal sealed class TerminalImage : IDisposable
{
    private readonly Dictionary<(int W, int H), Rgb[]> _pixelCache = [];
    private readonly Dictionary<(int W, int H, SKEncodedImageFormat Format), byte[]> _encodedCache = [];
    private bool[,]? _qrModules;
    private bool _qrParsed;

    public TerminalImage(string key, SKBitmap bitmap, bool crisp)
    {
        Key = key;
        Bitmap = bitmap;
        Crisp = crisp;
        Accent = ComputeAccent(bitmap);
    }

    public string Key { get; }

    public SKBitmap Bitmap { get; }

    /// <summary>
    ///     为 true 时使用最近邻缩放（二维码等需要锐利边缘的图片）。
    /// </summary>
    public bool Crisp { get; }

    public int Width => Bitmap.Width;

    public int Height => Bitmap.Height;

    /// <summary>
    ///     图片的主色调，用于沉浸页的强调色。
    /// </summary>
    public Rgb Accent { get; }

    public SKBitmap Resize(int width, int height)
    {
        var info = new SKImageInfo(Math.Max(1, width), Math.Max(1, height), SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var sampling = Crisp
            ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);
        return Bitmap.Resize(info, sampling) ?? Bitmap.Copy(SKColorType.Rgba8888);
    }

    /// <summary>
    ///     返回缩放到指定尺寸后的像素（透明像素与黑色混合）。
    /// </summary>
    public Rgb[] GetPixels(int width, int height)
    {
        if (_pixelCache.TryGetValue((width, height), out var cached))
        {
            return cached;
        }

        using var resized = Resize(width, height);
        var pixels = new Rgb[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = resized.GetPixel(x, y);
                var a = c.Alpha / 255f;
                pixels[y * width + x] = new Rgb((byte)(c.Red * a), (byte)(c.Green * a), (byte)(c.Blue * a));
            }
        }

        if (_pixelCache.Count > 8)
        {
            _pixelCache.Clear();
        }

        _pixelCache[(width, height)] = pixels;
        return pixels;
    }

    public byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        var key = (width, height, format);
        if (_encodedCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        using var resized = Resize(width, height);
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(format, format == SKEncodedImageFormat.Jpeg ? 90 : 100);
        var bytes = data.ToArray();
        if (_encodedCache.Count > 6)
        {
            _encodedCache.Clear();
        }

        _encodedCache[key] = bytes;
        return bytes;
    }

    /// <summary>
    ///     将二维码位图还原为模块矩阵；不是二维码时返回 null。
    /// </summary>
    public bool[,]? GetQrModules()
    {
        if (!_qrParsed)
        {
            _qrParsed = true;
            _qrModules = QrMatrixExtractor.Extract(Bitmap);
        }

        return _qrModules;
    }

    private static Rgb ComputeAccent(SKBitmap bitmap)
    {
        try
        {
            using var small = bitmap.Resize(new SKImageInfo(16, 16), new SKSamplingOptions(SKFilterMode.Linear));
            if (small == null)
            {
                return Rgb.FromHex(0x3BA4FF);
            }

            double r = 0, g = 0, b = 0, weightSum = 0;
            for (var y = 0; y < small.Height; y++)
            {
                for (var x = 0; x < small.Width; x++)
                {
                    var c = small.GetPixel(x, y);
                    var max = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
                    var min = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                    var saturation = max == 0 ? 0 : (max - min) / (double)max;
                    var weight = 0.05 + saturation * saturation * (max / 255.0);
                    r += c.Red * weight;
                    g += c.Green * weight;
                    b += c.Blue * weight;
                    weightSum += weight;
                }
            }

            var accent = new Rgb((byte)(r / weightSum), (byte)(g / weightSum), (byte)(b / weightSum));
            // 保证在深色背景上可读。
            return accent.Luminance < 0.45f ? accent.Mix(new Rgb(255, 255, 255), 0.45f - accent.Luminance + 0.15f) : accent;
        }
        catch
        {
            return Rgb.FromHex(0x3BA4FF);
        }
    }

    public void Dispose()
    {
        Bitmap.Dispose();
    }
}
