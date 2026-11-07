using System.Runtime.InteropServices;
using Avalonia;
using AvaloniaSilkEffects.Lumiere;
using AvaloniaSilkEffects.Tempera;
using AvaloniaSilkEffects.Tempera.Compositions;
using AvaloniaSilkEffects.Tempera.Geometry;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Tests;

public sealed class TemperaTests
{
    internal static IReadOnlyList<LumiereLine> SampleLines()
    {
        static LumiereLine Line(string text, double start, double end, params (string Text, double Start, double End)[] words) =>
            new(text, start, end, [.. words.Select(word => new LumiereWord(word.Text, word.Start, word.End))]);
        return
        [
            Line("我想你在每个夜晚", 1.0, 4.0, ("我", 1.0, 1.3), ("想", 1.3, 1.6), ("你", 1.6, 2.0), ("在", 2.0, 2.3), ("每个", 2.3, 2.9),
                ("夜晚", 2.9, 4.0)),
            Line("城市的灯火慢慢熄灭", 4.4, 7.6, ("城市的", 4.4, 5.3), ("灯火", 5.3, 6.0), ("慢慢", 6.0, 6.8), ("熄灭", 6.8, 7.6)),
            Line("Hold me close tonight", 8.0, 10.6, ("Hold ", 8.0, 8.5), ("me ", 8.5, 8.9), ("close ", 8.9, 9.6), ("tonight", 9.6, 10.6)),
            Line("风吹过记忆的海岸", 11.0, 14.0, ("风", 11.0, 11.4), ("吹过", 11.4, 12.0), ("记忆的", 12.0, 13.0), ("海岸", 13.0, 14.0)),
            Line("我们曾经说好的未来", 18.5, 22.0, ("我们", 18.5, 19.2), ("曾经", 19.2, 19.9), ("说好的", 19.9, 21.0), ("未来", 21.0, 22.0)),
            Line("像星光一样闪耀！", 22.4, 25.4, ("像", 22.4, 22.7), ("星光", 22.7, 23.5), ("一样", 23.5, 24.2), ("闪耀", 24.2, 25.0),
                ("！", 25.0, 25.4)),
            Line("就算明天不再来", 25.8, 28.6, ("就算", 25.8, 26.6), ("明天", 26.6, 27.4), ("不再来", 27.4, 28.6)),
            Line("也要唱完这首歌", 29.0, 32.0, ("也要", 29.0, 29.8), ("唱完", 29.8, 30.6), ("这首歌", 30.6, 32.0)),
        ];
    }

    [Fact]
    public void EveryShotKindHasProfileAndComposition()
    {
        Assert.Equal(121, TemperaShotProfiles.Kinds.Count);
        Assert.Equal(121, TemperaShotProfiles.Kinds.Distinct().Count());
        Assert.All(TemperaShotProfiles.Kinds, kind => Assert.Contains(kind, TemperaCompositions.RegisteredKinds));
    }

    [Fact]
    public void ProgramIsDeterministicAndTiled()
    {
        var first = TemperaProgramCompiler.Compile(SampleLines(), "seed-a");
        var second = TemperaProgramCompiler.Compile(SampleLines(), "seed-a");
        Assert.Equal(first.Paragraphs.SelectMany(p => p.Shots).Select(s => s.Kind), second.Paragraphs.SelectMany(p => p.Shots).Select(s => s.Kind));
        Assert.True(first.Paragraphs.Count >= 2, "The 4.5 s gap should cut a paragraph.");
        foreach (var paragraph in first.Paragraphs)
            for (var index = 1; index < paragraph.Shots.Count; index++)
                Assert.True(paragraph.Shots[index].StartTime >= paragraph.Shots[index - 1].StartTime);
        // A bridge shot carries the instrumental gap between 14 s and 18.5 s.
        Assert.Contains(first.Paragraphs.SelectMany(p => p.Shots), shot => shot.IsBridge);
    }

    [Fact]
    public void SingleHanWordsStayTogether()
    {
        var segments = TemperaProgramCompiler.BuildSegments(SampleLines()[0]);
        Assert.Contains(segments, segment => segment.Text == "我");
        var program = TemperaProgramCompiler.Compile([SampleLines()[0]], "glue");
        var first = program.Paragraphs[0].Shots[0].Slices[0];
        var line = program.Paragraphs[0].Lines[0];
        var text = string.Concat(line.Segments.Skip(first.SegmentStart).Take(first.SegmentEnd - first.SegmentStart).Select(s => s.Text));
        Assert.StartsWith("我想你", text);
    }

    [Fact]
    public void EarcutCutsHoles()
    {
        double[] outer = [0, 0, 100, 0, 100, 100, 0, 100];
        double[] hole = [25, 25, 75, 25, 75, 75, 25, 75];
        var data = outer.Concat(hole).ToArray();
        var triangles = Earcut.Triangulate(data, [4]);
        double area = 0;
        for (var i = 0; i < triangles.Count; i += 3)
        {
            double ax = data[triangles[i] * 2], ay = data[triangles[i] * 2 + 1];
            double bx = data[triangles[i + 1] * 2], by = data[triangles[i + 1] * 2 + 1];
            double cx = data[triangles[i + 2] * 2], cy = data[triangles[i + 2] * 2 + 1];
            area += Math.Abs((bx - ax) * (cy - ay) - (cx - ax) * (by - ay)) / 2;
        }
        Assert.Equal(10000 - 2500, area, 6);
    }

    [Fact]
    public void PaletteKeepsToneLadderOrdered()
    {
        var palette = TemperaPalette.Resolve(TemperaTheme.MidnightDefault, TemperaColorMode.Duo);
        Assert.True(palette.Tone1.Luminance < palette.Tone2.Luminance);
        Assert.True(palette.Tone2.Luminance < palette.Tone3.Luminance);
        Assert.True(palette.Tone3.Luminance < palette.Tone4.Luminance);
        Assert.True(Math.Abs(palette.Ink.Luminance - palette.Paper.Luminance) >= 96);
    }
}

/// <summary>Tempera on a real CGL context. TEMPERA_CAPTURE_DIR writes PNG frames for visual review.</summary>
public sealed class TemperaOpenGlTests
{
    private const string Framework = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    [DllImport(Framework)] private static extern int CGLChoosePixelFormat(int[] attributes, out nint format, out int count);
    [DllImport(Framework)] private static extern int CGLCreateContext(nint format, nint share, out nint context);
    [DllImport(Framework)] private static extern int CGLSetCurrentContext(nint context);
    [DllImport(Framework)] private static extern nint CGLGetCurrentContext();
    [DllImport(Framework)] private static extern int CGLDestroyContext(nint context);
    [DllImport(Framework)] private static extern int CGLDestroyPixelFormat(nint format);

    internal static void WithGl(Action<GL> body)
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

    private static readonly EffectColor Shell = new(0.035f, 0.035f, 0.043f);

    private static string? CaptureDirectory()
    {
        var directory = Environment.GetEnvironmentVariable("TEMPERA_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return null;
        Directory.CreateDirectory(directory);
        return directory;
    }

    [MacOpenGlFact]
    public void SceneRendersAndSeeksDeterministically() => WithGl(gl =>
    {
        using var device = new EffectDevice(gl);
        using var target = new EffectFramebuffer(gl);
        var scaling = double.TryParse(Environment.GetEnvironmentVariable("TEMPERA_SCALE"), out var parsed) ? parsed : 1;
        var size = new PixelSize((int)(1280 * scaling), (int)(720 * scaling));
        target.EnsureSize(size.Width, size.Height);
        var program = TemperaProgramCompiler.Compile(TemperaTests.SampleLines(), "kugou:sample");
        var scene = new TemperaEffectScene(new TemperaSongContext("kugou:sample", program, TemperaTheme.MidnightDefault,
            new TemperaSongMetadata("夜空中最亮的星", "示例歌手", "示例专辑")));
        scene.Initialize(device);
        scene.Resize(size, scaling);
        try
        {
            byte[] Capture(double seconds)
            {
                device.Render(scene, new EffectFrame(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1 / 60d), size, scaling, 0),
                    (int)target.Framebuffer, Shell);
                return Read(gl, size.Width, size.Height);
            }

            var first = Capture(2.6);
            Assert.Contains(first, value => value > 120);
            if (CaptureDirectory() is { } directory)
            {
                File.WriteAllLines(Path.Combine(directory, "program.txt"), program.Paragraphs.SelectMany(paragraph =>
                    new[] { $"{paragraph.Id} {paragraph.Kind} {paragraph.StartTime:F2}-{paragraph.EndTime:F2} out={paragraph.TransitionOut}" }
                        .Concat(paragraph.Shots.Select(shot =>
                            $"  {shot.Kind} {shot.StartTime:F2}-{shot.EndTime:F2} lyricEnd={shot.LyricEndTime:F2} flow={shot.FlowAngle:F2} motif={shot.Decor.Motif}{(shot.IsBridge ? " bridge" : "")}"))));
                var times = (Environment.GetEnvironmentVariable("TEMPERA_CAPTURE_TIMES") ?? "1.2,1.6,2.6,3.8,5.2,7.4,9.0,12.5,15.5,17.8,20.5,23.6,27.2,30.5,33.4,35.5")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(double.Parse).ToArray();
                var frames = times.Select(Capture).ToList();
                for (var index = 0; index < times.Length; index++)
                    CaptureImages.Save(frames[index], size, Path.Combine(directory, $"tempera-{times[index]:00.00}.png"));
                CaptureImages.SaveSheet(frames, size, Path.Combine(directory, "sheet.png"));
            }
            Capture(9.5);
            Capture(30);
            Assert.True(first.SequenceEqual(Capture(2.6)), "Seeking back must restore exactly the same frame.");
        }
        finally { scene.DisposeGpuResources(); }
    });

    [MacOpenGlFact]
    public void MetricsIncludeCustomDrawsUploadsAndReleaseAllGpuStorage() => WithGl(gl =>
    {
        var device = new EffectDevice(gl);
        var ledger = device.Gpu.Resources;
        using (device)
        using (var target = new EffectFramebuffer(gl))
        {
            var size = new PixelSize(640, 360);
            target.EnsureSize(size.Width, size.Height);
            var scene = new TemperaEffectScene(new TemperaSongContext("metrics",
                TemperaProgramCompiler.Compile(TemperaTests.SampleLines(), "metrics"),
                TemperaTheme.MidnightDefault, new TemperaSongMetadata("metrics")));
            scene.Initialize(device);
            scene.Resize(size, 1);
            try
            {
                device.Render(scene, new EffectFrame(TimeSpan.FromSeconds(2.6), TimeSpan.FromSeconds(1d / 60), size, 1, 0),
                    (int)target.Framebuffer, Shell);
                var metrics = device.FrameMetrics;
                Assert.True(scene.LastDrawCalls > 0);
                Assert.Equal(scene.LastDrawCalls + metrics.Flushes, metrics.DrawCalls);
                Assert.True(metrics.UploadedBytes > device.Primitives.FrameUploadedBytes);
                Assert.True(metrics.CreatedTextures > 0);
                Assert.True(metrics.GpuResources.RenderTargets > 1);
                Assert.True(metrics.GpuResources.BufferBytes > 0);
                Assert.True(metrics.Timings.TextPreparationMilliseconds > 0);
                Assert.True(metrics.Timings.PostProcessMilliseconds > 0);
                var t = metrics.Timings;
                // Stopwatch.GetElapsedTime rounds each interval to TimeSpan ticks independently.
                Assert.InRange(Math.Abs(t.CpuMilliseconds - (t.PrepareMilliseconds + t.UpdateMilliseconds + t.DrawMilliseconds +
                    t.PostProcessMilliseconds + t.CollectMilliseconds)), 0, 0.001);
                Assert.Equal(GLEnum.NoError, gl.GetError());
            }
            finally { scene.DisposeGpuResources(); }
        }
        Assert.Equal(default, ledger.Snapshot);
    });

    /// <summary>Frame cost at 2560 × 1440 (Retina, scale 2); runs only with TEMPERA_BENCH=1 and prints the timings.</summary>
    [MacOpenGlFact]
    public void BenchmarkRetinaFrames() => WithGl(gl =>
    {
        if (Environment.GetEnvironmentVariable("TEMPERA_BENCH") != "1") return;
        using var device = new EffectDevice(gl);
        using var target = new EffectFramebuffer(gl);
        var size = new PixelSize(2560, 1440);
        target.EnsureSize(size.Width, size.Height);
        var program = TemperaProgramCompiler.Compile(TemperaTests.SampleLines(), "bench");
        var scene = new TemperaEffectScene(new TemperaSongContext("bench", program, TemperaTheme.MidnightDefault, new TemperaSongMetadata("t")),
            new TemperaSceneOptions { EnableTextPrewarm = Environment.GetEnvironmentVariable("EFFECT_TEXT_PREWARM") != "0" });
        scene.Initialize(device);
        scene.Resize(size, 2);
        try
        {
            var report = new EffectBenchmarkReport();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var frames = 0;
            var worst = 0d;
            var slow = new List<(double T, double Ms)>();
            for (var t = 0.5; t < 33; t += 1 / 30d)
            {
                var start = watch.Elapsed.TotalMilliseconds;
                device.Render(scene, new EffectFrame(TimeSpan.FromSeconds(t), TimeSpan.FromSeconds(1 / 30d), size, 2, 0), (int)target.Framebuffer, Shell);
                gl.Finish();
                var cost = watch.Elapsed.TotalMilliseconds - start;
                report.Add(t, device, cost);
                worst = Math.Max(worst, cost);
                slow.Add((t, cost));
                frames++;
            }
            watch.Stop();
            report.Write("tempera", device, size, 2);
            Console.WriteLine($"TEMPERA bench: {frames} frames, mean {watch.Elapsed.TotalMilliseconds / frames:F2} ms, worst {worst:F2} ms, draws {scene.LastDrawCalls}; slowest " +
                string.Join(", ", slow.OrderByDescending(entry => entry.Ms).Take(8).Select(entry => $"{entry.T:F2}s={entry.Ms:F0}ms")));
        }
        finally { scene.DisposeGpuResources(); }
    });

    /// <summary>Every composition settled under one lyric line, as an 11 × 11 contact sheet (TEMPERA_CAPTURE_DIR/catalog.png).</summary>
    [MacOpenGlFact]
    public void CatalogRenders() => WithGl(gl =>
    {
        using var device = new EffectDevice(gl);
        using var target = new EffectFramebuffer(gl);
        var size = new PixelSize(640, 360);
        target.EnsureSize(size.Width, size.Height);
        var frames = new List<byte[]>();
        var line = new LumiereLine("凝彩落在字上", 1.0, 3.0, [new LumiereWord("凝彩", 1.0, 1.8), new LumiereWord("落在", 1.8, 2.4),
            new LumiereWord("字上", 2.4, 3.0)]);
        var other = new LumiereLine("下一句歌词", 30, 32, []);
        foreach (var kind in TemperaShotProfiles.Kinds)
        {
            var compiled = TemperaProgramCompiler.Compile([line, other], $"catalog:{kind}", wholeLineLyrics: true);
            var paragraph = compiled.Paragraphs[0];
            var shot = paragraph.Shots[0];
            var forced = new TemperaShot
            {
                Id = shot.Id,
                Kind = kind,
                StartTime = shot.StartTime,
                EndTime = 20,
                LyricEndTime = shot.LyricEndTime,
                Slices = shot.Slices,
                Camera = shot.Camera,
                CameraEnd = shot.CameraEnd,
                FlowAngle = shot.FlowAngle,
                Decor = shot.Decor,
            };
            var program = compiled with { Paragraphs = [paragraph with { Shots = [forced], EndTime = 20, TransitionOut = null }] };
            var scene = new TemperaEffectScene(new TemperaSongContext($"catalog:{kind}", program, TemperaTheme.MidnightDefault, new TemperaSongMetadata()));
            scene.Initialize(device);
            scene.Resize(size, 1);
            try
            {
                device.Render(scene, new EffectFrame(TimeSpan.FromSeconds(6), TimeSpan.Zero, size, 1, 0), (int)target.Framebuffer, Shell);
                frames.Add(Read(gl, size.Width, size.Height));
            }
            finally { scene.DisposeGpuResources(); }
        }
        if (CaptureDirectory() is { } directory) CaptureImages.SaveGrid(frames, size, 11, Path.Combine(directory, "catalog.png"));
    });
}
