using System.Numerics;
using System.Runtime.Versioning;
using Avalonia;
using AvaloniaSilkEffects.Gpu;
using AvaloniaSilkEffects.Gpu.Metal;

namespace AvaloniaSilkEffects.Tests;

[SupportedOSPlatform("macos")]
public sealed class MetalAttachmentTests
{
    [MacOpenGlFact]
    public void StencilMasksSurviveTargetSwitchesWithoutDepthAndKeepFourSampleEdges()
    {
        using var gpu = new MetalGpuDevice();
        using var device = new EffectDevice(gpu, ownsGpu: false);
        using var output = gpu.CreateRenderTarget(depthStencil: false);
        using var scratch = gpu.CreateRenderTarget(depthStencil: false);
        var scene = new MaskScene(scratch);
        scene.Initialize(device);
        device.PostProcess.MultisampleCount = 4;
        foreach (var size in new[] { new PixelSize(320, 180), new PixelSize(640, 360) })
        {
            output.EnsureSize(size.Width, size.Height);
            scratch.EnsureSize(size.Width, size.Height);
            scene.Resize(size, 1);
            device.Render(scene, new EffectFrame(TimeSpan.Zero, TimeSpan.Zero, size, 1, 0), output, EffectColor.Transparent);
            var pixels = gpu.ReadPixels(output);
            Assert.Equal(4, device.FrameMetrics.MultisampleCount);
            for (var y = 0; y < size.Height; y++)
            {
                var edge = (y * size.Width + size.Width / 2) * 4;
                Assert.Equal(255, pixels[edge - 1]); // left neighbor's alpha
                Assert.Equal(255, pixels[edge - 4]); // red draw after returning to the masked target
                Assert.InRange(pixels[edge + 3], 1, 254); // fractional coverage survived the MSAA resolve
                Assert.Equal(0, pixels[edge + 7]);
            }
        }
    }

    private sealed class MaskScene(GpuRenderTarget scratch) : EffectScene
    {
        public override void Render(EffectRenderContext context)
        {
            var gpu = context.Device.Gpu;
            var size = new Vector2(context.PixelSize.Width, context.PixelSize.Height);
            using var mask = context.PushStencilMask(primitives =>
                primitives.DrawRectangle(Matrix3x2.Identity, new(size.X / 2 + .25f, size.Y), EffectColor.White));
            context.Primitives.DrawRectangle(Matrix3x2.Identity, size, EffectColor.White);
            context.Primitives.Flush();
            var masked = gpu.Target!;
            gpu.SetTarget(scratch);
            gpu.Clear(EffectColor.Transparent);
            context.Primitives.DrawRectangle(Matrix3x2.Identity, size, EffectColor.White);
            context.Primitives.Flush();
            gpu.SetTarget(masked);
            context.Primitives.DrawRectangle(Matrix3x2.Identity, size, new(1, 0, 0));
        }
    }
}
