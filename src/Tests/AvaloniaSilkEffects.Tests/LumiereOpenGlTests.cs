using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using AvaloniaSilkEffects.Lumiere;
using AvaloniaSilkEffects.Lumiere.Light;
using AvaloniaSilkEffects.Lumiere.Rigs;
using AvaloniaSilkEffects.Lumiere.Text;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Tests;

public sealed class LumiereTests
{
    [Fact]
    public void RngSkipMatchesSequentialStream()
    {
        var sequential = new LumiereRng("lumiere:seed");
        for (var i = 0; i < 37; i++) sequential.Next();
        var skipped = LumiereRng.At("lumiere:seed", 37);
        for (var i = 0; i < 8; i++) Assert.Equal(sequential.Next(), skipped.Next());
    }

    [Fact]
    public void RngValuesStayInUnitInterval()
    {
        var rng = new LumiereRng("x");
        for (var i = 0; i < 10000; i++)
        {
            var value = rng.Next();
            Assert.InRange(value, 0, 0.9999999999);
        }
    }

    [Fact]
    public void CatalogHasTenFamiliesOfTenProfilesThatAllResolve()
    {
        Assert.Equal(100, LumiereCatalog.Profiles.Count);
        Assert.Equal(100, LumiereCatalog.Kinds.Distinct().Count());
        Assert.Equal(10, LumiereCatalog.Profiles.Select(profile => profile.Family).Distinct().Count());
        foreach (var profile in LumiereCatalog.Profiles)
        {
            var rig = profile.Light(new LumiereRigContext(16 / 9.0, new LumiereRng($"test:{profile.Kind}:light")));
            Assert.InRange(rig.Beams.Count, 1, LumiereLight.MaxBeams);
            var art = profile.LineArt(new LumiereRigContext(16 / 9.0, new LumiereRng($"test:{profile.Kind}:art")));
            Assert.True(art.Paths.Count + art.Nodes.Count > 0, $"{profile.Kind} has no line art.");
            Assert.All(art.Paths, path => Assert.All(path.Points, point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y))));
        }
    }

    [Fact]
    public void KeywordsMatchCjkPhrasesAndWholeEnglishWords()
    {
        var red = new EffectColor(1, 0, 0);
        var blue = new EffectColor(0, 0, 1);
        var matchers = LumiereKeywords.Prepare([new LumiereWordColor("花火", red), new LumiereWordColor("night", blue)], true);
        // 「花火」 colors both its glyphs, never the 火 of 火车.
        var cjk = LumiereKeywords.GlyphColors("花火和火车", matchers);
        Assert.Equal([true, true, false, false, false], cjk.Select(color => color is not null));
        // English matches whole words only, case-insensitively; spaces stay uncolored.
        var latin = LumiereKeywords.GlyphColors("Night nights night", matchers);
        Assert.Equal(new System.Numerics.Vector3(0, 0, 1), latin[0]);
        Assert.Null(latin[6]);
        Assert.Null(latin[5]);
        Assert.Null(latin[12]);
        Assert.NotNull(latin[13]);
        // Disabled keyword coloring matches nothing.
        Assert.Empty(LumiereKeywords.Prepare([new LumiereWordColor("花火", red)], false));
    }

    [Fact]
    public void KeywordLightKeepsTheLightBrightness()
    {
        var light = new System.Numerics.Vector3(1, 0.9f, 0.8f);
        var tinted = LumiereKeywords.Light(light, new System.Numerics.Vector3(0.1f, 0.1f, 0.4f), 0.6f);
        Assert.Equal(1, MathF.Max(tinted.X, MathF.Max(tinted.Y, tinted.Z)), 3);
        Assert.True(tinted.Z > tinted.X, "A dark blue keyword still reads blue in the light.");
    }

    [Fact]
    public void BeamIsBrightOnAxisAndDarkBehindSource()
    {
        var beam = LumiereLight.Resolve(LumiereRigBase.Shaft, 0, 1.6, new LumiereLightDrive(1, 0, Vector3.One));
        Assert.True(LumiereLight.BeamMask(beam, 0.8, 0.3) > 0.1);
        Assert.Equal(0, LumiereLight.BeamMask(beam, 0.8, -0.2));
        Assert.Equal(0, LumiereLight.BeamMask(beam, 0.1, 0.3));
    }
}

/// <summary>Lumiere on a real CGL context. LUMIERE_CAPTURE_DIR writes PNG frames for visual review.</summary>
public sealed class LumiereOpenGlTests
{
    private const string Framework = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    [DllImport(Framework)] private static extern int CGLChoosePixelFormat(int[] attributes, out nint format, out int count);
    [DllImport(Framework)] private static extern int CGLCreateContext(nint format, nint share, out nint context);
    [DllImport(Framework)] private static extern int CGLSetCurrentContext(nint context);
    [DllImport(Framework)] private static extern nint CGLGetCurrentContext();
    [DllImport(Framework)] private static extern int CGLDestroyContext(nint context);
    [DllImport(Framework)] private static extern int CGLDestroyPixelFormat(nint format);

    private static unsafe void WithGl(Action<GL> body)
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
                body(gl);
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

    private static unsafe byte[] Read(GL gl, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        fixed (byte* p = pixels)
            gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        Assert.Equal(GLEnum.NoError, gl.GetError());
        return pixels;
    }

    /// <summary>The GPU light field and the CPU lightAt must agree, or glyphs light up where no beam is drawn.</summary>
    [MacOpenGlFact]
    public void LightFieldMatchesCpuBeamFormula() => WithGl(gl =>
    {
        const int width = 640, height = 400;
        using var device = new EffectDevice(gl);
        using var target = new LumiereRenderTarget(gl);
        using var field = new LumiereLightField(gl);
        target.EnsureSize(width, height);
        target.Bind(clear: true);
        // No fog modulation (tyndall = 1), no ambient, no glare: the pixel is exactly 1 − exp(−Σ mask).
        var rig = new LumiereLightRig(
            [
                LumiereRigBase.Shaft with { Gobo = new(LumiereGoboPattern.Blinds, 9, 0.5, 0.3) },
                LumiereRigBase.Fan with { X = 0.2, Angle = 1.2 },
                LumiereRigBase.Shaft with { X = 0.8, Spread = -0.05, Width = 0.12, Reach = 0.7 },
            ],
            new LumiereFogSpec(0, 1, 2.4, 0, 0, 0, 0), null);
        var frame = new LumiereLightFieldFrame { Rig = rig, Time = 3.7, Octaves = 5 };
        LumiereLight.ResolveBeams(rig, 3.7, width / (double)height, new LumiereLightDrive(1, 0.4, Vector3.One), frame.Beams);
        field.Render(frame, Matrix3x2.Identity, new Vector2(width, height), width, height, 0);
        var pixels = Read(gl, width, height);

        var maxError = 0.0;
        var lit = 0;
        for (var py = 3; py < height; py += 7)
        for (var px = 5; px < width; px += 11)
        {
            var expected = 255 * LumiereLight.CompressLight(LumiereLight.LightAt(frame.Beams, (px + 0.5) / height, (py + 0.5) / height));
            var actual = pixels[((height - 1 - py) * width + px) * 4];
            if (expected > 20) lit++;
            maxError = Math.Max(maxError, Math.Abs(actual - expected));
        }
        Assert.True(lit > 200, $"Only {lit} lit samples; the test rig should cover the frame.");
        // 8-bit quantization (0.5) + dither (0.5) + float32 vs float64 on the streak / gobo noise.
        Assert.True(maxError <= 2.5, $"GPU and CPU light differ by {maxError:F2}/255.");
    });

    [MacOpenGlFact]
    public void SceneRendersSeeksAndStaysPremultiplied() => WithGl(gl =>
    {
        using var device = new EffectDevice(gl);
        using var target = new EffectFramebuffer(gl);
        var size = new PixelSize(1280, 800);
        target.EnsureSize(size.Width, size.Height);
        var scene = CreateScene();
        scene.Initialize(device);
        scene.Resize(size, 1);
        try
        {
            byte[] Capture(double seconds, EffectColor? clear = null)
            {
                scene.Audio = new LumiereAudioFrame(0.4, 0.5, 0.3);
                device.Render(scene, new EffectFrame(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1 / 60d), size, 1, 0),
                    (int)target.Framebuffer, clear ?? new EffectColor(0.051f, 0.071f, 0.208f));
                return Read(gl, size.Width, size.Height);
            }

            var first = Capture(4.2);
            Assert.Contains(first, value => value > 160);
            var directory = Environment.GetEnvironmentVariable("LUMIERE_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllLines(Path.Combine(directory, "program.txt"), [
                    $"lyricEnd={scene.Program.LyricEndTime:F2} duration={scene.Program.Duration:F2}",
                    .. scene.Program.Paragraphs.SelectMany(paragraph => new[]
                        {
                            $"P{paragraph.Index} {paragraph.Kind} {paragraph.StartTime:F2}-{paragraph.EndTime:F2} opening={paragraph.Opening} out={paragraph.TransitionOut}",
                        }.Concat(paragraph.Shots.Select(shot => $"  {shot.Kind} {shot.StartTime:F2}-{shot.EndTime:F2} lines=[{string.Join(",", shot.LineIndices)}]{(shot.IsBridge ? " bridge" : "")}"))),
                ]);
                var times = (Environment.GetEnvironmentVariable("LUMIERE_CAPTURE_TIMES") ?? "1.3,2.6,4.2,6.4,9.1,13.6,17.2,24.5,30.2,34.6,42,46")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(double.Parse).ToArray();
                var frames = times.Select(time => Capture(time)).ToList();
                for (var index = 0; index < times.Length; index++)
                    CaptureImages.Save(frames[index], size, Path.Combine(directory, $"lumiere-{times[index]:00.00}.png"));
                CaptureImages.SaveSheet(frames, size, Path.Combine(directory, "sheet.png"));
            }

            foreach (var time in new[] { 2.2, 13.6, 42.0 })
            {
                var transparent = Capture(time, EffectColor.Transparent);
                for (var index = 0; index < transparent.Length; index += 4)
                {
                    var alpha = transparent[index + 3];
                    Assert.True(transparent[index] <= alpha + 1 && transparent[index + 1] <= alpha + 1 &&
                        transparent[index + 2] <= alpha + 1,
                        $"Pixel {index / 4} at {time}s is not premultiplied: {transparent[index]},{transparent[index + 1]},{transparent[index + 2]},{alpha}.");
                }
            }
            Capture(9.5);
            Capture(1.0);
            Assert.True(first.SequenceEqual(Capture(4.2)), "Seeking back must restore exactly the same frame.");
        }
        finally { scene.DisposeGpuResources(); }
    });

    /// <summary>Every profile at one instant, as a 10 × 10 contact sheet (LUMIERE_CAPTURE_DIR/catalog.png).</summary>
    [MacOpenGlFact]
    public void CatalogGalleryRenders() => WithGl(gl =>
    {
        using var device = new EffectDevice(gl);
        using var target = new EffectFramebuffer(gl);
        var size = new PixelSize(480, 270);
        target.EnsureSize(size.Width, size.Height);
        var frames = new List<byte[]>();
        foreach (var profile in LumiereCatalog.Profiles)
        {
            var lines = new[] { new LumiereLine("光落在字上", 4.5, 5.75, []), new LumiereLine("下一句歌词", 7, 8.25, []) };
            var scene = new LumiereEffectScene(new LumiereSongContext($"gallery:{profile.Kind}", SingleShot(profile.Kind, lines, 20),
                new LumiereTheme(new EffectColor(0.051f, 0.071f, 0.208f), FontFamily: "PingFang SC", FontWeight: 600), new LumiereSongMetadata()),
                new LumiereSceneOptions { Tuning = new LumiereSceneTuning { OverlayFrame = false } });
            scene.Initialize(device);
            scene.Resize(size, 1);
            try
            {
                device.Render(scene, new EffectFrame(TimeSpan.FromSeconds(6), TimeSpan.Zero, size, 1, 0), (int)target.Framebuffer,
                    new EffectColor(0.051f, 0.071f, 0.208f));
                var pixels = Read(gl, size.Width, size.Height);
                Assert.Contains(pixels, value => value > 90);
                frames.Add(pixels);
            }
            finally { scene.DisposeGpuResources(); }
        }
        var directory = Environment.GetEnvironmentVariable("LUMIERE_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            CaptureImages.SaveGrid(frames, size, 10, Path.Combine(directory, "catalog.png"));
        }
    });

    /// <summary>Frame cost at 2560 × 1600 (Retina, scale 2); runs only with LUMIERE_BENCH=1 and prints the timings.</summary>
    [MacOpenGlFact]
    public void BenchmarkRetinaFrames() => WithGl(gl =>
    {
        if (Environment.GetEnvironmentVariable("LUMIERE_BENCH") != "1") return;
        using var device = new EffectDevice(gl);
        using var target = new EffectFramebuffer(gl);
        var size = new PixelSize(2560, 1600);
        target.EnsureSize(size.Width, size.Height);
        var seamless = Environment.GetEnvironmentVariable("LUMIERE_CAPTURE_SEAMLESS") != "0";
        var program = LumiereProgramCompiler.Compile(CaptureLines(), "bench", 52, seamless);
        var variant = Environment.GetEnvironmentVariable("LUMIERE_VARIANT") ?? "";
        float Param(string key, float fallback) => variant.Split('_').Select(part => part.Split('=')).Where(kv => kv.Length == 2 && kv[0] == key)
            .Select(kv => float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(fallback).First();
        var tuning = new LumiereSceneTuning
        {
            Trails = true,
            Bloom = variant.Contains("nobloom") ? 0 : Param("b", 1),
            TextBloom = variant.Contains("notextbloom") ? 0 : Param("tb", 1),
            TextOnly = variant.Contains("textonly"),
            LightIntensity = variant.Contains("nolight") ? 0 : Param("li", 1),
            ShowText = !variant.Contains("notext"),
        };
        var scene = new LumiereEffectScene(new LumiereSongContext("bench", program, CaptureTheme with { FontWeight = (int)Param("w", 600) }, new LumiereSongMetadata("夜航星", "Lumiere", "Capture")),
            new LumiereSceneOptions { Tuning = tuning });
        scene.Initialize(device);
        scene.Resize(size, 2);
        try
        {
            var report = new List<string>();
            foreach (var start in new[] { 3.0, 14.0, 30.0 })
            {
                for (var i = 0; i < 20; i++) Render(start + i / 60.0);
                gl.Finish();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                const int frames = 120;
                for (var i = 0; i < frames; i++) Render(start + (20 + i) / 60.0);
                gl.Finish();
                report.Add($"t={start}: {watch.Elapsed.TotalMilliseconds / frames:F2} ms/frame (cpu+gpu)");
            }
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "lumiere-bench.txt"), report);
            if (Environment.GetEnvironmentVariable("LUMIERE_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                foreach (var t in new[] { 3.0, 9.0, 14.0, 22.0, 30.0 })
                {
                    Render(t);
                    var pixels = new byte[size.Width * size.Height * 4];
                    unsafe { fixed (byte* p = pixels) gl.ReadPixels(0, 0, (uint)size.Width, (uint)size.Height, Silk.NET.OpenGL.PixelFormat.Rgba, Silk.NET.OpenGL.PixelType.UnsignedByte, p); }
                    CaptureImages.Save(pixels, size, Path.Combine(directory, $"retina{variant}-{t:00}.png"));
                }
            }
            void Render(double t) => device.Render(scene, new EffectFrame(TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(1 / 60d), size, 2, 0),
                (int)target.Framebuffer, new EffectColor(0.051f, 0.071f, 0.208f));
        }
        finally { scene.DisposeGpuResources(); }
    });

    internal static LumiereTheme CaptureTheme { get; } = new(new EffectColor(0.051f, 0.071f, 0.208f), new EffectColor(1, 1, 1),
        new EffectColor(0.42f, 0.45f, 0.57f), new EffectColor(0.55f, 0.59f, 0.72f), "PingFang SC", 600);

    /// <summary>The Sonnet capture lyrics (14 lines, a longer gap every 4) as Lumiere lines.</summary>
    internal static IReadOnlyList<LumiereLine> CaptureLines() => [.. SonnetSceneCaptureTests.BuildLines().Select(line =>
        new LumiereLine(line.FullText, line.StartTime, line.EndTime, [.. line.Words.Select(word => new LumiereWord(word.Text, word.StartTime, word.EndTime))]))];

    private static LumiereEffectScene CreateScene()
    {
        var seamless = Environment.GetEnvironmentVariable("LUMIERE_CAPTURE_SEAMLESS") == "1";
        var program = LumiereProgramCompiler.Compile(CaptureLines(), "capture", 52, seamless);
        // Keywords as an AI theme would write them, so captures show keyword coloring.
        var theme = CaptureTheme with
        {
            WordColors = [new LumiereWordColor("大海", new EffectColor(0.3f, 0.6f, 1)), new LumiereWordColor("答案", new EffectColor(1, 0.45f, 0.35f)),
                new LumiereWordColor("night", new EffectColor(0.6f, 0.5f, 1)), new LumiereWordColor("明天", new EffectColor(0.4f, 1, 0.6f))],
        };
        return new LumiereEffectScene(new LumiereSongContext("capture", program, theme, new LumiereSongMetadata("夜航星", "Lumiere", "Capture")));
    }

    /// <summary>A program of one paragraph with a single shot of <paramref name="kind"/> (gallery tiles).</summary>
    internal static LumiereProgram SingleShot(string kind, IReadOnlyList<LumiereLine> lines, double end) => new("gallery", false, 0, end, null,
    [
        new LumiereParagraph("gallery", 0, LumiereParagraphKind.Verse, LumiereParagraphBoundary.SongStart, 0, end, end,
            [.. Enumerable.Range(0, lines.Count)], lines,
            [new LumiereProgramShot("gallery-shot", kind, [.. Enumerable.Range(0, lines.Count)], 0, end, end, false)], null, false),
    ]);
}

internal static class CaptureImages
{
    public static void Save(byte[] pixels, PixelSize size, string path)
    {
        using var bitmap = ToBitmap(pixels, size);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        encoded.SaveTo(output);
    }

    public static SkiaSharp.SKBitmap ToBitmap(byte[] pixels, PixelSize size)
    {
        var bitmap = new SkiaSharp.SKBitmap(size.Width, size.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        for (var y = 0; y < size.Height; y++)
            Marshal.Copy(pixels, (size.Height - 1 - y) * size.Width * 4, bitmap.GetPixels() + y * bitmap.RowBytes, size.Width * 4);
        return bitmap;
    }

    public static void SaveGrid(IReadOnlyList<byte[]> frames, PixelSize size, int columns, string path)
    {
        var rows = (frames.Count + columns - 1) / columns;
        using var sheet = new SkiaSharp.SKBitmap(size.Width * columns, size.Height * rows);
        using (var canvas = new SkiaSharp.SKCanvas(sheet))
        {
            canvas.Clear(SkiaSharp.SKColors.Black);
            for (var index = 0; index < frames.Count; index++)
            {
                using var bitmap = ToBitmap(frames[index], size);
                canvas.DrawBitmap(bitmap, index % columns * size.Width, index / columns * size.Height);
            }
        }
        using var encoded = SkiaSharp.SKImage.FromBitmap(sheet).Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        using var output = File.Create(path);
        encoded.SaveTo(output);
    }

    public static void SaveSheet(IReadOnlyList<byte[]> frames, PixelSize size, string path)
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
}
