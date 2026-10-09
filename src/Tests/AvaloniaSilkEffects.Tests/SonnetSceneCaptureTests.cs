using System.Runtime.InteropServices;
using Avalonia;
using AvaloniaSilkEffects.Sonnet;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Tests;

/// <summary>
/// Renders the full player-configured Sonnet scene on a real CGL context. Always
/// asserts that rendering is error-free and seek-stable; when SONNET_CAPTURE_DIR
/// is set it also writes PNG frames for visual review.
/// </summary>
public sealed class SonnetSceneCaptureTests
{
    private const string Framework = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    [DllImport(Framework)] private static extern int CGLChoosePixelFormat(int[] attributes, out nint format, out int count);
    [DllImport(Framework)] private static extern int CGLCreateContext(nint format, nint share, out nint context);
    [DllImport(Framework)] private static extern int CGLSetCurrentContext(nint context);
    [DllImport(Framework)] private static extern nint CGLGetCurrentContext();
    [DllImport(Framework)] private static extern int CGLDestroyContext(nint context);
    [DllImport(Framework)] private static extern int CGLDestroyPixelFormat(nint format);

    [MacOpenGlFact]
    public unsafe void PlayerSceneRendersAndSeeksOnRealGl()
    {
        var previous = CGLGetCurrentContext();
        Assert.Equal(0, CGLChoosePixelFormat([99, 0x4100, 73, 0], out var format, out _));
        nint native = 0;
        try
        {
            Assert.Equal(0, CGLCreateContext(format, 0, out native));
            Assert.Equal(0, CGLSetCurrentContext(native));
            var library = NativeLibrary.Load(Framework);
            try
            {
                using var gl = GL.GetApi(name => NativeLibrary.TryGetExport(library, name, out var address) ? address : 0);
                using var device = new EffectDevice(gl);
                using var target = new EffectFramebuffer(gl);
                var size = new PixelSize(1280, 800);
                target.EnsureSize(size.Width, size.Height);
                var scene = CreatePlayerScene();
                scene.Initialize(device);
                scene.Resize(size, 1);
                try
                {
                    var background = new EffectColor(0.051f, 0.071f, 0.208f, 1);
                    byte[] Capture(double seconds) => CaptureWith(seconds, background);
                    byte[] CaptureWith(double seconds, EffectColor clear)
                    {
                        // Constant audio: icon reactivity smooths across frames like Folia,
                        // so only a steady signal keeps seeking frame-exact.
                        scene.Audio = new(0.45f, 0.5f, 0.4f);
                        var frame = new EffectFrame(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1 / 60d), size, 1, 0);
                        device.Render(scene, frame, (int)target.Framebuffer, clear);
                        var pixels = new byte[size.Width * size.Height * 4];
                        fixed (byte* p = pixels)
                            gl.ReadPixels(0, 0, (uint)size.Width, (uint)size.Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
                        Assert.Equal(GLEnum.NoError, gl.GetError());
                        return pixels;
                    }

                    // Warm caches (paragraph builds are amortised over frames).
                    for (var t = 0.0; t < 2; t += 0.25) Capture(t);
                    var first = Capture(1.2);
                    var directory = Environment.GetEnvironmentVariable("SONNET_CAPTURE_DIR");
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                        File.WriteAllLines(Path.Combine(directory, "program.txt"), scene.Program.Paragraphs.SelectMany(paragraph =>
                            new[] { $"P {paragraph.Kind} {paragraph.StartTime:F2}-{paragraph.EndTime:F2} out={paragraph.TransitionOut}" }
                                .Concat(paragraph.Shots.Select(shot => $"  S {shot.Kind} {shot.StartTime:F2}-{shot.EndTime:F2}"))));
                        var times = (Environment.GetEnvironmentVariable("SONNET_CAPTURE_TIMES") ?? "1.2,3.1,5.4,8.3,11,14.2,17.5,21,26,30.5")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(double.Parse);
                        var frames = new List<byte[]>();
                        foreach (var time in times)
                        {
                            Capture(time - 0.1);
                            var pixels = Capture(time);
                            frames.Add(pixels);
                            Save(pixels, size, Path.Combine(directory, $"sonnet-{time:00.00}.png"));
                        }
                        SaveSheet(frames, size, Path.Combine(directory, "sheet.png"));
                    }
                    // Bloom and zoom blur add light over a transparent clear; the result
                    // must stay valid premultiplied color for the compositor.
                    foreach (var time in new[] { 1.2, 11.6, 21.3 })
                    {
                        var transparent = CaptureWith(time, EffectColor.Transparent);
                        for (var index = 0; index < transparent.Length; index += 4)
                        {
                            var alpha = transparent[index + 3];
                            Assert.True(transparent[index] <= alpha + 1 && transparent[index + 1] <= alpha + 1 &&
                                transparent[index + 2] <= alpha + 1, $"Pixel {index / 4} at {time}s is not premultiplied: {transparent[index]},{transparent[index + 1]},{transparent[index + 2]},{alpha}.");
                        }
                    }
                    Capture(0.5);
                    Capture(1.0);
                    Assert.True(first.SequenceEqual(Capture(1.2)), "Seeking back must restore exactly the same frame.");
                }
                finally { scene.DisposeGpuResources(); }
            }
            finally { NativeLibrary.Free(library); }
        }
        finally
        {
            CGLSetCurrentContext(previous);
            if (native != 0) CGLDestroyContext(native);
            CGLDestroyPixelFormat(format);
        }
    }

    private static void Save(byte[] pixels, PixelSize size, string path)
    {
        using var bitmap = ToBitmap(pixels, size);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        encoded.SaveTo(output);
    }

    [MacOpenGlFact]
    public unsafe void CoreGeometryVariantsRenderOnRealGl()
    {
        var previous = CGLGetCurrentContext();
        Assert.Equal(0, CGLChoosePixelFormat([99, 0x4100, 73, 0], out var format, out _));
        nint native = 0;
        try
        {
            Assert.Equal(0, CGLCreateContext(format, 0, out native));
            Assert.Equal(0, CGLSetCurrentContext(native));
            var library = NativeLibrary.Load(Framework);
            try
            {
                using var gl = GL.GetApi(name => NativeLibrary.TryGetExport(library, name, out var address) ? address : 0);
                using var device = new EffectDevice(gl);
                using var target = new EffectFramebuffer(gl);
                var size = new PixelSize(1600, 800);
                target.EnsureSize(size.Width, size.Height);
                var scene = new GalleryScene();
                scene.Initialize(device);
                scene.Resize(size, 1);
                device.Render(scene, new EffectFrame(TimeSpan.Zero, TimeSpan.Zero, size, 1, 0),
                    (int)target.Framebuffer, new EffectColor(0.051f, 0.071f, 0.208f));
                var pixels = new byte[size.Width * size.Height * 4];
                fixed (byte* p = pixels)
                    gl.ReadPixels(0, 0, (uint)size.Width, (uint)size.Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
                Assert.Equal(GLEnum.NoError, gl.GetError());
                Assert.Contains(pixels, value => value > 120);
                var directory = Environment.GetEnvironmentVariable("SONNET_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    Save(pixels, size, Path.Combine(directory, "gallery.png"));
                }
            }
            finally { NativeLibrary.Free(library); }
        }
        finally
        {
            CGLSetCurrentContext(previous);
            if (native != 0) CGLDestroyContext(native);
            CGLDestroyPixelFormat(format);
        }
    }

    private sealed class GalleryScene : EffectScene
    {
        private readonly EffectContainer _root = new();

        public GalleryScene()
        {
            var variants = Enumerable.Range(0, 100).Where(SonnetCoreDrawLists.Handles).ToArray();
            for (var index = 0; index < variants.Length; index++)
            {
                // Each cell is drawn at a 480px viewport and scaled into a 200px tile.
                var cell = SonnetCoreDrawLists.Build(variants[index], 480, 480, 0x5eed1234u + (uint)index * 977,
                    0xffffff, 0x8e96b8).Replay(48);
                cell.Scale = new System.Numerics.Vector2(200f / 480 * 0.62f);
                cell.Position = new System.Numerics.Vector2(100 + index % 8 * 200, 100 + index / 8 * 200);
                _root.Add(cell);
                _root.Add(new TextNode { Text = variants[index].ToString(), FontFamily = "Menlo", FontSize = 12,
                    Color = new EffectColor(1, 0.85f, 0.3f), Position = new System.Numerics.Vector2(8 + index % 8 * 200, 4 + index / 8 * 200) });
            }
        }

        public override void Render(EffectRenderContext context) => context.Render(_root);
    }

    private static SkiaSharp.SKBitmap ToBitmap(byte[] pixels, PixelSize size)
    {
        var bitmap = new SkiaSharp.SKBitmap(size.Width, size.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        for (var y = 0; y < size.Height; y++)
            Marshal.Copy(pixels, (size.Height - 1 - y) * size.Width * 4, bitmap.GetPixels() + y * bitmap.RowBytes, size.Width * 4);
        return bitmap;
    }

    private static void SaveSheet(IReadOnlyList<byte[]> frames, PixelSize size, string path)
    {
        const int columns = 2;
        var cellWidth = size.Width / 2;
        var cellHeight = size.Height / 2;
        var rows = (frames.Count + columns - 1) / columns;
        using var sheet = new SkiaSharp.SKBitmap(cellWidth * columns, cellHeight * rows);
        using (var canvas = new SkiaSharp.SKCanvas(sheet))
        {
            canvas.Clear(SkiaSharp.SKColors.Black);
            using var paint = new SkiaSharp.SKPaint { IsAntialias = true };
            for (var index = 0; index < frames.Count; index++)
            {
                using var bitmap = ToBitmap(frames[index], size);
                using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                var x = index % columns * cellWidth;
                var y = index / columns * cellHeight;
                canvas.DrawImage(image, new SkiaSharp.SKRect(x, y, x + cellWidth, y + cellHeight),
                    new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear), paint);
            }
        }
        using var encoded = SkiaSharp.SKImage.FromBitmap(sheet).Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        using var output = File.Create(path);
        encoded.SaveTo(output);
    }

    private static SonnetScene CreatePlayerScene()
    {
        var theme = new SonnetTheme(
            Background: new(0.051f, 0.071f, 0.208f, 1),
            Primary: new(1, 1, 1, 1),
            Accent: new(0.55f, 0.59f, 0.72f, 1),
            Secondary: new(0.42f, 0.45f, 0.57f, 1),
            FontFamily: "PingFang SC",
            FontWeight: 600);
        var lines = Environment.GetEnvironmentVariable("SONNET_CAPTURE_INSTRUMENTAL") == "1"
            ? SonnetStaffView.VirtualLines()
            : BuildLines();
        var program = SonnetProgramCompiler.Compile(lines, "capture");
        var context = new SonnetSongContext("capture", "capture", program, theme, new("夜航星", "Sonnet", "Capture"));
        return new SonnetScene(context, new SonnetSceneOptions
        {
            // Same as the player, which composites over its own background layer.
            TransparentBackground = true,
            Tuning = new SonnetTuning
            {
                TextureResolution = 1.5f,
                PostProcessEnabled = true,
                PostProcessGrain = 0,
                PostProcessVignette = 0,
                ShowChromaticSplit = false,
                PostProcessRgbShift = 0,
                PostProcessLensDispersion = 0,
            },
        });
    }

    internal static IReadOnlyList<SonnetLine> BuildLines()
    {
        var texts = new[]
        {
            "我曾经跨过山和大海", "也穿过人山和人海", "我曾经拥有着的一切", "转眼都飘散如烟",
            "我曾经失落失望", "失掉所有方向", "直到看见平凡", "才是唯一的答案",
            "当你仍然还在幻想", "你的明天", "她会好吗 还是更烂", "对我而言是另一天",
            "I keep on running through the night", "Under the falling stars",
        };
        var output = new List<SonnetLine>();
        var start = 0.4;
        for (var index = 0; index < texts.Length; index++)
        {
            var text = texts[index];
            var graphemes = text.Contains(' ') && text.Any(char.IsAsciiLetter)
                ? text.Split(' ')
                : text.Select(c => c.ToString()).Where(c => c != " ").ToArray();
            var per = 0.24;
            var words = graphemes.Select((word, i) => new SonnetWordTiming(word, start + i * per, start + (i + 1) * per)).ToArray();
            var end = start + graphemes.Length * per + 0.35;
            output.Add(new(text, start, end, words));
            start = end + (index % 4 == 3 ? 1.6 : 0.15);
        }
        return output;
    }
}
