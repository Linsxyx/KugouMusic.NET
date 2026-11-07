using AvaloniaSilkEffects.Gpu.OpenGl;
using Avalonia;
using AvaloniaSilkEffects.Lumiere;
using AvaloniaSilkEffects.Lumiere.Text;
using AvaloniaSilkEffects.Tempera;
using AvaloniaSilkEffects.Tempera.Rendering;
using AvaloniaSilkEffects.Tempera.Text;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace AvaloniaSilkEffects.Tests;

public sealed class TextWarmupOpenGlTests
{
    [MacOpenGlFact]
    public void NativeVectorUploadPreservesPixelsWithoutAnImageSizedManagedCopy() => TemperaOpenGlTests.WithGl(gl =>
    {
        using var gpu = new GlGpuDevice(gl);
        using var cache = new EffectTextureCache(gpu);
        cache.GetOrCreateVector("warm", new(8), 1, c => c.Clear(SKColors.White));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var texture = cache.GetOrCreateVector("large", new(1024), 1, c => c.Clear(new SKColor(80, 120, 160, 255)));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 128 * 1024);
        var fbo = gl.GenFramebuffer();
        try
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture.Handle, 0);
            var pixels = Read(gl, new(1024, 1024));
            for (var offset = 0; offset < pixels.Length; offset += 4)
                Assert.True(pixels[offset] == 80 && pixels[offset + 1] == 120 && pixels[offset + 2] == 160 && pixels[offset + 3] == 255);
        }
        finally { gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0); gl.DeleteFramebuffer(fbo); }
    });

    [MacOpenGlFact]
    public void TemperaSharesMeasurementsAndGlyphsAndRecreatesReleasedTextures() => TemperaOpenGlTests.WithGl(gl =>
    {
        using var gpu = new GlGpuDevice(gl);
        using var cache = new EffectTextureCache(gpu);
        var glyphs = new TemperaGlyphCache(cache);
        const string text = "中文 AV 👩‍🚀 words wrap";
        var estimated = glyphs.EstimateBytes(text, "PingFang SC", 600, 32, 180, 2);
        var first = glyphs.Get(text, "PingFang SC", 600, 32, 2, 180);
        Assert.Equal(estimated, first.Texture.EstimatedBytes);
        cache.TextResources.BeginFrame();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            Assert.Same(first.Texture, glyphs.Get(text, "PingFang SC", 600, 32, 2, 180).Texture);
            glyphs.Box(text, "PingFang SC", 600, 32, 180);
        }
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 1024);
        Assert.Equal(0, cache.TextResources.TextWork.MeasureMilliseconds);
        Assert.NotSame(first.Texture, glyphs.Get(text, "PingFang SC", 600, 32, 1, 180).Texture);
        Assert.NotSame(first.Texture, glyphs.Get(text, "PingFang SC", 400, 32, 2, 180).Texture);
        cache.Release(first.Texture);
        Assert.NotSame(first.Texture, glyphs.Get(text, "PingFang SC", 600, 32, 2, 180).Texture);
        Assert.Equal(glyphs.Box(text, "PingFang SC", 600, 32, 180), TemperaGlyphs.Box(text, "PingFang SC", 600, 32, 180));
    });

    [MacOpenGlFact]
    public void TemperaWarmupSpreadsWorkAndRespectsStorageAndCurrentDraws() => TemperaOpenGlTests.WithGl(gl =>
    {
        using var device = new EffectDevice(gl);
        using var renderer = new TemperaRenderer(device.Gpu, device.Primitives, device.Textures);
        var root = new TemperaGroup();
        for (var i = 0; i < 5; i++) root.Add(new TemperaText { Text = "预热" + i, FontFamily = "PingFang SC", FontWeight = 600, FontSize = 32 });
        var resources = device.Textures.TextResources;
        var warmed = 0;
        for (var frame = 0; frame < 40 && warmed < 5; frame++)
        {
            resources.BeginFrame(); device.Textures.BeginFrame();
            renderer.PrewarmText(root, 2);
            Assert.InRange(resources.FrameCreatedTextures, 0, 1);
            Assert.InRange(resources.TextWork.WarmedBytes, 0, EffectTextWarmup.MaximumTextureBytes);
            warmed += resources.FrameCreatedTextures;
        }
        Assert.Equal(5, warmed);
        var blocked = new TemperaGroup();
        var next = blocked.Add(new TemperaText { Text = "新歌词", FontFamily = "PingFang SC", FontWeight = 600, FontSize = 32 });
        resources.BeginFrame();
        device.Textures.CreateRgba(new byte[4], 1, 1);
        renderer.PrewarmText(blocked, 2);
        Assert.Null(next.Glyph);
        resources.BeginFrame();
        Assert.False(EffectTextWarmup.Fits(EffectTextWarmup.MaximumTextureBytes + 1, device.Textures));
        device.Textures.ResidentTextureLimit = 1;
        Assert.False(EffectTextWarmup.Fits(4, device.Textures));
        device.Textures.ResidentTextureLimit = 1024;
        device.Textures.ResidentByteLimit = 1;
        renderer.PrewarmText(blocked, 2);
        Assert.Equal(0, resources.FrameCreatedTextures);
        Assert.Null(next.Glyph);
        renderer.ResetWarmup();
        device.Textures.ResidentByteLimit = 256 * 1024 * 1024;
        resources.BeginFrame();
        renderer.PrewarmText(new TemperaGroup(), 1);
        Assert.Null(next.Glyph);
    });

    [MacOpenGlFact]
    public void LumiereEstimatesExactTextureStorageIncludingGraphemeSpacing() => TemperaOpenGlTests.WithGl(gl =>
    {
        foreach (var text in new[] { "", "AVTo 字形", "👩‍🚀e\u0301 日本語" })
        foreach (var scale in new[] { 1f, 2f })
        {
            using var gpu = new GlGpuDevice(gl);
            using var line = new LumiereGlyphLine(gpu, text, 43.5f, "PingFang SC", 600, scale, 0.04f);
            Assert.Equal(line.Texture.EstimatedBytes, LumiereGlyphLine.EstimateBytes(text, 43.5f, "PingFang SC", 600, scale,
                LumiereReveal.Graphemes(text).Count, 0.04f));
        }
    });

    [MacOpenGlFact]
    public void PrewarmingPreservesFramesThroughSeeksSongChangesAndResize() => TemperaOpenGlTests.WithGl(gl =>
    {
        var lumiereProgram = LumiereProgramCompiler.Compile(LumiereOpenGlTests.CaptureLines(), "parity", 52, true);
        var temperaProgram = TemperaProgramCompiler.Compile(TemperaTests.SampleLines(), "parity");
        var lumiereSong = new LumiereSongContext("parity", lumiereProgram, LumiereOpenGlTests.CaptureTheme, new("song"));
        var temperaSong = new TemperaSongContext("parity", temperaProgram, TemperaTheme.MidnightDefault, new("song"));
        Check(enabled => new LumiereEffectScene(lumiereSong, new() { EnableTextPrewarm = enabled }),
            scene => ((LumiereEffectScene)scene).SetSong(lumiereSong with { Seed = "replacement" }));
        Check(enabled => new TemperaEffectScene(temperaSong, new() { EnableTextPrewarm = enabled }),
            scene => ((TemperaEffectScene)scene).SetSong(temperaSong with { Seed = "replacement" }));

        void Check(Func<bool, EffectScene> create, Action<EffectScene> swap)
        {
            var cold = Capture(false);
            var warm = Capture(true);
            Assert.Equal(cold.Frames.Count, warm.Frames.Count);
            for (var i = 0; i < cold.Frames.Count; i++)
                Assert.True(cold.Frames[i].SequenceEqual(warm.Frames[i]), $"Prewarming changed frame {i}.");
            Assert.True(warm.Warmed > 0, "The comparison must exercise actual prewarmed textures.");

            (List<byte[]> Frames, int Warmed) Capture(bool enabled)
            {
                using var device = new EffectDevice(gl);
                using var target = new EffectFramebuffer(gl);
                var scene = create(enabled);
                scene.Initialize(device);
                var result = new List<byte[]>();
                var warmed = 0;
                try
                {
                    var size = new PixelSize(640, 360);
                    scene.Resize(size, 1); target.EnsureSize(size.Width, size.Height);
                    double[] times = [0.5, 0.6, 0.7, 0.8, 0.9, 1, 3.3, 3.4, 3.5, 3.6, 4.4, 7.1, 7.2, 7.3, 8, 10.3, 10.4, 10.5, 11, 17.5, 18.5, 29, 31.1, 2.6];
                    for (var i = 0; i < times.Length + 4; i++)
                    {
                        var scaling = i < times.Length ? 1 : 2;
                        if (i == times.Length)
                        {
                            size = new(800, 450); scene.Resize(size, scaling); target.EnsureSize(size.Width, size.Height);
                        }
                        if (i == times.Length + 2) swap(scene);
                        var time = i < times.Length ? times[i] : 4.2;
                        device.Render(scene, new(TimeSpan.FromSeconds(time), TimeSpan.FromSeconds(1d / 60), size, scaling, 0), (int)target.Framebuffer, EffectColor.Transparent);
                        var work = device.FrameMetrics.Timings.TextWork;
                        Assert.InRange(work.WarmedTextures, 0, 1);
                        Assert.InRange(work.WarmedBytes, 0, EffectTextWarmup.MaximumTextureBytes);
                        warmed += work.WarmedTextures;
                        result.Add(Read(gl, size));
                    }
                }
                finally { scene.DisposeGpuResources(); }
                return (result, warmed);
            }
        }
    });

    private static unsafe byte[] Read(GL gl, PixelSize size)
    {
        var pixels = new byte[size.Width * size.Height * 4];
        fixed (byte* p = pixels) gl.ReadPixels(0, 0, (uint)size.Width, (uint)size.Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        Assert.Equal(GLEnum.NoError, gl.GetError());
        return pixels;
    }
}
