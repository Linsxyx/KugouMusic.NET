using KgTest.Graphics;
using KgTest.Models;
using KgTest.Tui;
using KgTest.Views;
using SkiaSharp;

namespace KgTest;

// 命令行自检与预览：支持 --self-test 验证图片解码/二维码提取/全部协议编码/离屏画布渲染，--preview-image 在终端直接预览图片。

internal static class CliDiagnostics
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains("--preview-image", StringComparer.OrdinalIgnoreCase))
        {
            var idx = Array.FindIndex(args, a => string.Equals(a, "--preview-image", StringComparison.OrdinalIgnoreCase));
            var source = idx + 1 < args.Length ? args[idx + 1] : null;
            return await PreviewImageAsync(source);
        }

        return RunSelfTest();
    }

    private static int RunSelfTest()
    {
        // 1. 构造合成二维码位图并测试模块还原。
        const int modules = 25;
        const int scale = 8;
        const int border = 16;
        var qrSize = modules * scale + border * 2;
        using var qrBitmap = new SKBitmap(qrSize, qrSize);
        using (var canvas = new SKCanvas(qrBitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };
            DrawFinder(canvas, paint, border, border, scale);
            DrawFinder(canvas, paint, border + (modules - 7) * scale, border, scale);
            DrawFinder(canvas, paint, border, border + (modules - 7) * scale, scale);
            for (var y = 8; y < modules - 8; y++)
            {
                for (var x = 8; x < modules - 8; x++)
                {
                    if ((x + y) % 2 == 0)
                    {
                        canvas.DrawRect(border + x * scale, border + y * scale, scale, scale, paint);
                    }
                }
            }
        }

        var extracted = QrMatrixExtractor.Extract(qrBitmap);
        if (extracted == null || extracted.GetLength(0) != modules)
        {
            Console.Error.WriteLine("QR matrix extraction failed.");
            return 1;
        }

        // 2. 构造渐变封面图并验证三种图形协议编码与半块字符绘制。
        using var coverBitmap = BuildSyntheticCover(240, 240);
        using var qrImage = new TerminalImage("test:qr", qrBitmap.Copy(), crisp: true);
        using var coverImage = new TerminalImage("test:cover", coverBitmap.Copy(), crisp: false);

        var sb = new System.Text.StringBuilder();
        ImageProtocolEncoder.AppendKitty(sb, coverImage, 1, 20, 10, 160, 160);
        ImageProtocolEncoder.AppendITerm(sb, coverImage, 20, 10, 160, 160);
        ImageProtocolEncoder.AppendSixel(sb, coverImage, 120, 120);
        if (sb.Length < 512)
        {
            Console.Error.WriteLine("Image protocol encoding produced unexpectedly short output.");
            return 1;
        }

        // 3. 在离屏画布上分别渲染二维码与沉浸页。
        var tui = new Canvas { ImageMode = TerminalImageMode.Blocks };
        var theme = new Theme();
        using var cache = new ImageCache(() => { });
        tui.Reset(80, 18);
        if (tui.Qr(new Rect(2, 1, 40, 16), qrImage).IsEmpty)
        {
            Console.Error.WriteLine("QR rendering failed.");
            return 1;
        }

        tui.Reset(128, 36);

        var snapshot = new PlaybackStateSnapshot
        {
            CurrentSong = new TerminalSongItem
            {
                Name = "测试曲目 - 终端图形渲染",
                Singer = "Kugou TUI",
                Source = "SelfTest",
                DurationSeconds = 215,
                IsPlaying = true
            },
            IsPlaying = true,
            Volume = 0.8f,
            Position = TimeSpan.FromSeconds(68),
            Duration = TimeSpan.FromSeconds(215),
            LyricLines =
            [
                new TerminalLyricLineView { Offset = -1, Primary = "上一句歌词" },
                new TerminalLyricLineView
                {
                    Offset = 0,
                    IsCurrent = true,
                    Primary = "正在播放的逐字高亮歌词",
                    Secondary = "Translated lyric line",
                    Words = [("正在播放的", 1f), ("逐字", 0.6f), ("高亮歌词", 0f)]
                },
                new TerminalLyricLineView { Offset = 1, Primary = "下一句歌词" }
            ],
            SpectrumBands = [0.2f, 0.5f, 0.8f, 0.6f, 0.9f, 0.4f, 0.7f, 0.3f],
            Rms = 0.12
        };

        var visualizer = new VisualizerView();
        visualizer.RenderImmersive(tui, snapshot, cache, theme);
        Console.WriteLine(tui.Snapshot());
        Console.WriteLine($"Self-test OK · Accent=#{coverImage.Accent.R:X2}{coverImage.Accent.G:X2}{coverImage.Accent.B:X2} · EncodedBytes={sb.Length}");
        return 0;
    }

    private static async Task<int> PreviewImageAsync(string? source)
    {
        SKBitmap? bitmap = null;
        if (string.IsNullOrWhiteSpace(source))
        {
            bitmap = BuildSyntheticCover(320, 320);
        }
        else if (File.Exists(source))
        {
            bitmap = SKBitmap.Decode(source);
        }
        else if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(source);
            bitmap = SKBitmap.Decode(bytes);
        }

        if (bitmap == null)
        {
            Console.Error.WriteLine("无法加载图片。");
            return 1;
        }

        using (bitmap)
        using (var image = new TerminalImage("preview", bitmap.Copy(), crisp: false))
        {
            var mode = TerminalCapabilities.DetectImageMode();
            var canvas = new Canvas { ImageMode = mode };
            canvas.Reset(80, 24);
            canvas.Image("preview", new Rect(2, 1, 44, 20), image);
            using var renderer = new TerminalRenderer
            {
                TrueColor = TerminalCapabilities.DetectTrueColor(),
                CellPixelSize = TerminalCapabilities.GetCellPixelSize()
            };
            renderer.Present(canvas);
            Console.WriteLine();
        }

        return 0;
    }

    private static void DrawFinder(SKCanvas canvas, SKPaint paint, int x, int y, int s)
    {
        canvas.DrawRect(x, y, 7 * s, 7 * s, paint);
        using var white = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        canvas.DrawRect(x + s, y + s, 5 * s, 5 * s, white);
        canvas.DrawRect(x + 2 * s, y + 2 * s, 3 * s, 3 * s, paint);
    }

    private static SKBitmap BuildSyntheticCover(int width, int height)
    {
        var bmp = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = (byte)(40 + x * 190 / width);
                var g = (byte)(60 + y * 150 / height);
                var b = (byte)(180 - x * 100 / width);
                bmp.SetPixel(x, y, new SKColor(r, g, b));
            }
        }

        return bmp;
    }
}
