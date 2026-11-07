using Avalonia;
using AvaloniaSilkEffects.Backgrounds;
using AvaloniaSilkEffects.Gpu;
using AvaloniaSilkEffects.Gpu.Metal;
using AvaloniaSilkEffects.Gpu.Vulkan;
using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan;
using AvaloniaSilkEffects.Lumiere;
using AvaloniaSilkEffects.Tempera;
using Silk.NET.OpenGL;
using Xunit.Abstractions;

namespace AvaloniaSilkEffects.Tests;

/// <summary>Renders the same frames through OpenGL and each native backend and compares the pixels.</summary>
public sealed class GpuBackendParityTests(ITestOutputHelper output)
{
    private static readonly EffectColor Shell = new(0.035f, 0.035f, 0.043f);
    private static readonly EffectColor Navy = new(0.051f, 0.071f, 0.208f);

    public static TheoryData<string> Scenes() => ["tempera", "lumiere", "sonnet", "latent"];

    [MacOpenGlTheory]
    [MemberData(nameof(Scenes))]
    public void MetalMatchesOpenGl(string kind)
    {
        var (size, times, clear) = Setup(kind);
        var expected = RenderGl(kind, size, times, clear);
        using var gpu = new MetalGpuDevice();
        var actual = RenderNative(gpu, kind, size, times, clear, target => gpu.ReadPixels(target));
        Compare(kind, "metal", size, times, expected, actual);
    }

    [VulkanTheory]
    [MemberData(nameof(Scenes))]
    public void VulkanMatchesOpenGl(string kind)
    {
        var (size, times, clear) = Setup(kind);
        var expected = RenderGl(kind, size, times, clear);
        using var gpu = VulkanGpuDevice.Create(new VulkanDeviceOptions { Api = VulkanTheoryAttribute.Loader() });
        output.WriteLine($"{gpu.Renderer} {gpu.ApiVersion}");
        var actual = RenderNative(gpu, kind, size, times, clear, target => gpu.ReadPixels(target));
        Compare(kind, "vulkan", size, times, expected, actual);
    }

    internal static (PixelSize Size, double[] Times, EffectColor Clear) Setup(string kind) => kind switch
    {
        "tempera" => (new PixelSize(960, 540), [1.2, 2.6, 5.2, 9.0, 15.5], Shell),
        "lumiere" => (new PixelSize(960, 600), [2.0, 4.2, 6.5], Navy),
        "sonnet" => (new PixelSize(960, 600), [0.5, 1.2, 3.0, 6.0], Navy),
        _ => (new PixelSize(480, 270), [0, 1.5, 4], EffectColor.Transparent),
    };

    internal static IEffectScene CreateScene(string kind) => kind switch
    {
        "tempera" => new TemperaEffectScene(new TemperaSongContext("kugou:sample",
            TemperaProgramCompiler.Compile(TemperaTests.SampleLines(), "kugou:sample"), TemperaTheme.MidnightDefault,
            new TemperaSongMetadata("夜空中最亮的星", "示例歌手", "示例专辑"))),
        "lumiere" => LumiereOpenGlTests.CreateScene(),
        "sonnet" => SonnetSceneCaptureTests.CreatePlayerScene(),
        _ => new LatentBackgroundScene(),
    };

    internal static void Prepare(IEffectScene scene)
    {
        switch (scene)
        {
            case LumiereEffectScene lumiere: lumiere.Audio = new LumiereAudioFrame(0.4, 0.5, 0.3); break;
            case Sonnet.SonnetScene sonnet: sonnet.Audio = new(0.45f, 0.5f, 0.4f); break;
            case LatentBackgroundScene latent: latent.Audio = new LatentAudio(); break;
        }
    }

    private static List<byte[]> RenderGl(string kind, PixelSize size, double[] times, EffectColor clear)
    {
        var frames = new List<byte[]>();
        TemperaOpenGlTests.WithGl(gl =>
        {
            using var device = new EffectDevice(gl);
            using var target = new EffectFramebuffer(gl);
            target.EnsureSize(size.Width, size.Height);
            frames = Run(device, kind, size, times, frame =>
            {
                device.Render(frame.Scene, frame.Frame, (int)target.Framebuffer, clear);
                return ReadGl(gl, size);
            });
        });
        return frames;
    }

    internal static List<byte[]> RenderNative(GpuDevice gpu, string kind, PixelSize size, double[] times, EffectColor clear,
        Func<GpuRenderTarget, byte[]> read)
    {
        var device = new EffectDevice(gpu);
        using var target = gpu.CreateRenderTarget(depthStencil: true);
        target.EnsureSize(size.Width, size.Height);
        var frames = Run(device, kind, size, times, frame =>
        {
            device.Render(frame.Scene, frame.Frame, target, clear);
            return read(target);
        });
        // The caller owns the GpuDevice; release everything else on it.
        device.Textures.Dispose();
        device.Primitives.Dispose();
        return frames;
    }

    private static List<byte[]> Run(EffectDevice device, string kind, PixelSize size, double[] times,
        Func<(IEffectScene Scene, EffectFrame Frame), byte[]> render)
    {
        var scene = CreateScene(kind);
        scene.Initialize(device);
        scene.Resize(size, 1);
        try
        {
            // Warm amortised caches the same way on both backends.
            for (var t = 0.0; t < 2; t += 0.5)
            {
                Prepare(scene);
                render((scene, Frame(t, size)));
            }
            var frames = new List<byte[]>();
            foreach (var time in times)
            {
                Prepare(scene);
                frames.Add(render((scene, Frame(time, size))));
            }
            return frames;
        }
        finally { scene.DisposeGpuResources(); }
    }

    private static EffectFrame Frame(double seconds, PixelSize size) =>
        new(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(1 / 60d), size, 1, 0);

    internal static unsafe byte[] ReadGl(GL gl, PixelSize size)
    {
        var pixels = new byte[size.Width * size.Height * 4];
        fixed (byte* p = pixels)
            gl.ReadPixels(0, 0, (uint)size.Width, (uint)size.Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        Assert.Equal(GLEnum.NoError, gl.GetError());
        return pixels;
    }

    internal void Compare(string kind, string backend, PixelSize size, double[] times, List<byte[]> expected, List<byte[]> actual)
    {
        var directory = Environment.GetEnvironmentVariable("GPU_PARITY_DIR");
        for (var index = 0; index < times.Length; index++)
        {
            var (a, b) = (expected[index], actual[index]);
            Assert.Equal(a.Length, b.Length);
            long total = 0;
            var off = 0;
            var max = 0;
            for (var i = 0; i < a.Length; i += 4)
            {
                var d = 0;
                for (var c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(a[i + c] - b[i + c]));
                total += d;
                max = Math.Max(max, d);
                if (d > 24) off++;
            }
            var pixels = a.Length / 4;
            var mean = total / (double)pixels;
            output.WriteLine($"{kind} t={times[index]}: mean {mean:F3}, max {max}, >24: {off * 100.0 / pixels:F3}%");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                CaptureImages.Save(a, size, Path.Combine(directory, $"{kind}-{times[index]:00.0}-gl.png"));
                CaptureImages.Save(b, size, Path.Combine(directory, $"{kind}-{times[index]:00.0}-{backend}.png"));
            }
            Assert.True(mean < 1.5, $"{kind} at {times[index]}s: mean difference {mean:F2}/255 against OpenGL.");
            Assert.True(off < pixels / 200, $"{kind} at {times[index]}s: {off} pixels differ by more than 24/255.");
        }
    }
}

/// <summary>Runs on macOS with a Vulkan loader (MoltenVK); VULKAN_LOADER overrides the library path.</summary>
public sealed class VulkanTheoryAttribute : TheoryAttribute
{
    private static readonly string[] Candidates =
        ["/opt/homebrew/lib/libvulkan.1.dylib", "/usr/local/lib/libvulkan.1.dylib"];

    public VulkanTheoryAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "Compared against CGL OpenGL on macOS.";
        else if (Path() is null) Skip = "No Vulkan loader (install molten-vk and vulkan-loader).";
    }

    private static string? Path() =>
        Environment.GetEnvironmentVariable("VULKAN_LOADER") is { Length: > 0 } custom ? custom : Candidates.FirstOrDefault(File.Exists);

    internal static Vk Loader() => new(new DefaultNativeContext(Path()!));
}

public sealed class MacOpenGlTheoryAttribute : TheoryAttribute
{
    public MacOpenGlTheoryAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "Requires macOS (CGL OpenGL and Metal).";
    }
}
