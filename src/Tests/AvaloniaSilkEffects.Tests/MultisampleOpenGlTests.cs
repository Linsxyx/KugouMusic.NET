using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using AvaloniaSilkEffects.Sonnet;
using Silk.NET.OpenGL;
using SkiaSharp;
using Xunit.Abstractions;

namespace AvaloniaSilkEffects.Tests;

public sealed class MultisampleOpenGlTests(ITestOutputHelper output)
{
    [MacOpenGlFact]
    public void AttachmentsHaveMatchingSamplesAndSurviveResizeAndDisposal()
    {
        WithGl(gl =>
        {
            using var source = new EffectFramebuffer(gl);
            using var resolved = new EffectFramebuffer(gl);
            foreach (var size in new[] { new PixelSize(320, 180), new PixelSize(640, 360) })
            {
                source.EnsureSize(size.Width, size.Height, 4);
                Assert.Equal(4, source.Samples);
                Assert.Equal(GLEnum.FramebufferComplete, gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
                foreach (var attachment in new[] { source.ColorBuffer, source.DepthStencilBuffer })
                {
                    gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, attachment);
                    gl.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.Samples, out var samples);
                    Assert.Equal(4, samples);
                }
                var handle = source.Framebuffer;
                source.EnsureSize(size.Width, size.Height, 4);
                Assert.Equal(handle, source.Framebuffer);
                resolved.EnsureSize(size.Width, size.Height);
                source.ResolveTo(resolved);
                gl.GetInteger(GetPName.Samples, out var resolvedSamples);
                Assert.Equal(0, resolvedSamples);
                Assert.Equal(GLEnum.NoError, gl.GetError());
            }
            var framebuffer = source.Framebuffer;
            var color = source.ColorBuffer;
            var depth = source.DepthStencilBuffer;
            source.Dispose();
            Assert.False(gl.IsFramebuffer(framebuffer));
            Assert.False(gl.IsRenderbuffer(color));
            Assert.False(gl.IsRenderbuffer(depth));
            source.EnsureSize(320, 180, 4);
            Assert.Equal(4, source.Samples);
        });
    }

    [MacOpenGlFact]
    public void ResolveSmoothsGeometryWithAndWithoutPostProcessing()
    {
        WithGl(gl =>
        {
            using var device = new EffectDevice(gl);
            using var target = new EffectFramebuffer(gl);
            var scene = new FrameFixture();
            scene.Initialize(device);
            foreach (var scale in new[] { 1, 2 })
            foreach (var postEnabled in new[] { false, true })
            {
                var size = new PixelSize(500 * scale, 320 * scale);
                target.EnsureSize(size.Width, size.Height);
                scene.Resize(size, scale);
                device.PostProcess.Reset();
                device.PostProcess.ResolutionScale = 1;
                device.PostProcess.UseSonnetPasses = true;
                if (postEnabled)
                {
                    // The player's existing material profile, including lens and grain.
                    device.PostProcess.LensDistortion = 0.3f;
                    device.PostProcess.Grain = 0.07f;
                    device.PostProcess.Vignette = 0.85f;
                    device.PostProcess.SonnetNoiseSeed = 0.5f;
                }
                device.PostProcess.MultisampleCount = 1;
                var single = Capture(gl, device, scene, target, size, scale);
                Assert.Equal(0, scene.ObservedSamples);
                device.PostProcess.MultisampleCount = 4;
                // A shared desktop context can leave MSAA disabled between callbacks.
                gl.Disable(EnableCap.Multisample);
                var multi = Capture(gl, device, scene, target, size, scale);
                Assert.Equal(4, scene.ObservedSamples);
                Assert.Equal(4, device.FrameMetrics.MultisampleCount);
                Assert.Equal(postEnabled, device.FrameMetrics.PostProcessingEnabled);
                Assert.False(single.SequenceEqual(multi));
                if (!postEnabled)
                {
                    var before = PartialLinePixels(single, size.Width, scale);
                    var after = PartialLinePixels(multi, size.Width, scale);
                    Assert.Equal(0, before);
                    Assert.True(after > 200 * scale, $"Expected antialiased edge coverage, got {after} pixels.");
                    for (var i = 0; i < multi.Length; i += 4)
                    {
                        Assert.True(multi[i] <= multi[i + 3] && multi[i + 1] <= multi[i + 3] && multi[i + 2] <= multi[i + 3],
                            "Resolved RGB must remain premultiplied, including partially covered edges.");
                    }
                    output.WriteLine($"{scale}x DPI: GL_SAMPLES={scene.ObservedSamples}, partial line pixels {before} -> {after}");
                }
                Save(single, size, $"single-post-{postEnabled}-dpi-{scale}");
                Save(multi, size, $"msaa4-post-{postEnabled}-dpi-{scale}");
                Assert.True(multi.SequenceEqual(Capture(gl, device, scene, target, size, scale)), "Repeated fixed frames must be identical.");
                device.PostProcess.MultisampleCount = 1;
                Assert.True(single.SequenceEqual(Capture(gl, device, scene, target, size, scale)), "Disabling MSAA must restore the single-sample path.");
            }
            scene.DisposeGpuResources();
        });
    }

    [MacOpenGlFact]
    public void MultisampleStencilMaskClipsBeforeResolve()
    {
        WithGl(gl =>
        {
            using var device = new EffectDevice(gl);
            using var target = new EffectFramebuffer(gl);
            var size = new PixelSize(500, 320);
            target.EnsureSize(size.Width, size.Height);
            var scene = new FrameFixture { ClipRightHalf = true };
            scene.Initialize(device);
            scene.Resize(size, 1);
            device.PostProcess.MultisampleCount = 4;
            var pixels = Capture(gl, device, scene, target, size, 1);
            Assert.Contains(pixels, pixel => pixel != 0);
            for (var y = 0; y < size.Height; y++)
            for (var x = size.Width / 2; x < size.Width; x++)
                Assert.Equal(0, pixels[(y * size.Width + x) * 4 + 3]);
        });
    }

    [MacOpenGlFact]
    public void SonnetRequestsMsaaEvenWhenFiltersAreDisabled()
    {
        WithGl(gl =>
        {
            using var device = new EffectDevice(gl);
            using var target = new EffectFramebuffer(gl);
            var size = new PixelSize(640, 360);
            target.EnsureSize(size.Width, size.Height);
            var theme = new SonnetTheme(EffectColor.Transparent, EffectColor.White, EffectColor.White, EffectColor.White);
            var program = SonnetProgramCompiler.Compile([new SonnetLine("沿着光的轨迹", 0, 10, [new("沿着光的轨迹", 0, 10)])], "msaa");
            var sonnet = new SonnetScene(program, theme);
            sonnet.Options.TransparentBackground = true;
            var scene = new RecordingScene(sonnet);
            scene.Initialize(device);
            scene.Resize(size, 1);
            try
            {
                foreach (var filters in new[] { false, true })
                {
                    sonnet.Tuning.PostProcessEnabled = filters;
                    var pixels = Capture(gl, device, scene, target, size, 1);
                    Assert.Equal(4, scene.ObservedSamples);
                    Assert.Equal(4, device.FrameMetrics.MultisampleCount);
                    Assert.Contains(pixels, pixel => pixel != 0);
                }
            }
            finally { scene.DisposeGpuResources(); }
        });
    }

    private static unsafe byte[] Capture(GL gl, EffectDevice device, IEffectScene scene, EffectFramebuffer target, PixelSize size, double scale)
    {
        var frame = new EffectFrame(TimeSpan.FromSeconds(2), TimeSpan.Zero, size, scale, 0);
        device.Render(scene, frame, (int)target.Framebuffer, EffectColor.Transparent);
        gl.GetInteger(GetPName.DrawFramebufferBinding, out var drawTarget);
        Assert.Equal((int)target.Framebuffer, drawTarget);
        var pixels = new byte[size.Width * size.Height * 4];
        fixed (byte* p = pixels)
            gl.ReadPixels(0, 0, (uint)size.Width, (uint)size.Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        Assert.Equal(GLEnum.NoError, gl.GetError());
        return pixels;
    }

    private static int PartialLinePixels(byte[] pixels, int width, int scale)
    {
        // OpenGL readback is bottom-up; the isolated white line occupies the top 80 logical pixels.
        return Enumerable.Range(pixels.Length / 4 - width * 80 * scale, width * 80 * scale)
            .Count(i => pixels[i * 4 + 3] is > 0 and < 255);
    }

    private static void Save(byte[] pixels, PixelSize size, string name)
    {
        var directory = Environment.GetEnvironmentVariable("SONNET_MSAA_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new SKBitmap(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (var y = 0; y < size.Height; y++)
            Marshal.Copy(pixels, (size.Height - 1 - y) * size.Width * 4, bitmap.GetPixels() + y * bitmap.RowBytes, size.Width * 4);
        using var preview = new SKBitmap(size.Width, size.Height);
        using var canvas = new SKCanvas(preview);
        canvas.Clear(new SKColor(17, 42, 115));
        canvas.DrawBitmap(bitmap, 0, 0);
        using var image = SKImage.FromBitmap(preview);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        data.SaveTo(file);
    }

    private sealed class FrameFixture : EffectScene
    {
        private readonly EffectContainer _root = new();
        public bool ClipRightHalf { get; init; }
        public int ObservedSamples { get; private set; }
        public FrameFixture()
        {
            _root.Add(new ShapeNode { Shape = EffectShapeKind.Line, Position = new(30, 35), Size = new(420, 16), StrokeWidth = 2.2f });
            var theme = new SonnetTheme(EffectColor.Transparent, EffectColor.White, EffectColor.White, EffectColor.White);
            var placement = SonnetDecorationTests.Placement(-0.045f) with { X = 250, Y = 192, MeasuredWidth = 360, MeasuredHeight = 110 };
            var decor = new SonnetFrameDecorView(placement, 64, theme, 2, 0, 0, 5);
            decor.Update(2);
            _root.Add(decor.Root);
            _root.Add(new TextNode { Text = "沿着光的轨迹", FontFamily = "PingFang SC", FontSize = 42,
                Position = new(250, 192), Anchor = new(.5f), Rotation = -.045f });
        }
        public override void Resize(PixelSize size, double scale) => _root.Scale = new((float)scale);
        public override void Render(EffectRenderContext context)
        {
            context.Device.Gl.GetInteger(GetPName.Samples, out var samples);
            ObservedSamples = samples;
            using var mask = ClipRightHalf ? context.PushStencilMask(primitives => primitives.DrawShape(
                new ShapeNode { Size = new(context.PixelSize.Width / 2f, context.PixelSize.Height) })) : null;
            context.Render(_root);
        }
    }

    private sealed class RecordingScene(IEffectScene inner) : IEffectScene
    {
        public int ObservedSamples { get; private set; }
        public void Initialize(EffectDevice device) => inner.Initialize(device);
        public void Resize(PixelSize size, double scale) => inner.Resize(size, scale);
        public void Update(in EffectFrame frame) => inner.Update(frame);
        public void DisposeGpuResources() => inner.DisposeGpuResources();
        public void Render(EffectRenderContext context)
        {
            context.Device.Gl.GetInteger(GetPName.Samples, out var samples);
            ObservedSamples = samples;
            inner.Render(context);
        }
    }

    private const string Framework = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    [DllImport(Framework)] private static extern int CGLChoosePixelFormat(int[] attributes, out nint format, out int count);
    [DllImport(Framework)] private static extern int CGLCreateContext(nint format, nint share, out nint context);
    [DllImport(Framework)] private static extern int CGLSetCurrentContext(nint context);
    [DllImport(Framework)] private static extern nint CGLGetCurrentContext();
    [DllImport(Framework)] private static extern int CGLDestroyContext(nint context);
    [DllImport(Framework)] private static extern int CGLDestroyPixelFormat(nint format);

    private void WithGl(Action<GL> action)
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
                output.WriteLine(gl.GetStringS(StringName.Renderer));
                action(gl);
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
}
