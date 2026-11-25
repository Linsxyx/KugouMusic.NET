using AvaloniaSilkEffects.Gpu;
using AvaloniaSilkEffects.Gpu.Vulkan;
using Silk.NET.Vulkan;

namespace AvaloniaSilkEffects.Tests;

/// <summary>Verifies Vulkan memory sub-allocation, idle reclamation, arena trimming and lazy external depth/stencil.</summary>
public sealed class VulkanMemoryTests
{
    [Fact]
    public void PoolBlockAlignsSplitsAndCoalescesFreeRanges()
    {
        var block = new VulkanMemoryAllocator.PoolBlock(default, 4096, 0, (true, 0));
        Assert.True(block.TryAllocate(100, 256, out var a));
        Assert.Equal(0UL, a);
        Assert.True(block.TryAllocate(100, 256, out var b));
        Assert.Equal(256UL, b);
        Assert.True(block.TryAllocate(200, 64, out var c));
        // Fits in the leading gap [100..256) aligned to 64 -> 128..328 doesn't fit, so placed at 384.
        Assert.Equal(384UL, c);
        Assert.Equal(3, block.LiveCount);

        // Free out of order and verify complete coalescing back to a single 4096-byte range.
        block.Free(b, 100);
        block.Free(a, 100);
        block.Free(c, 200);
        Assert.Equal(0, block.LiveCount);
        Assert.Equal(1, block.FreeRangeCount);

        Assert.True(block.TryAllocate(4096, 1, out var full));
        Assert.Equal(0UL, full);
        block.Free(full, 4096);
        Assert.Equal(0, block.LiveCount);
    }

    [VulkanFact]
    public void SmallTexturesSharePooledBlockAndReclaimIdleMemoryFreesAll()
    {
        using var gpu = VulkanGpuDevice.Create(new VulkanDeviceOptions { Api = VulkanTheoryAttribute.Loader() });
        var pixels = new byte[32 * 32 * 4];
        var textures = Enumerable.Range(0, 24)
            .Select(_ => gpu.CreateTexture(32, 32, pixels, GpuFilter.Linear))
            .ToArray();

        // 24 small 4 KiB textures share 1 image pool block, plus 1 buffer pool block for the frame arena chunk.
        Assert.Equal(2, gpu.MemoryAllocator.PooledBlockCount);
        Assert.True(gpu.ArenaBytes > 0);

        foreach (var texture in textures) texture.Dispose();
        Assert.True(gpu.PendingGarbageCount >= 24);

        gpu.ReclaimIdleMemory();
        Assert.Equal(0, gpu.PendingGarbageCount);
        Assert.Equal(0, gpu.ArenaBytes);
        // Buffer pool block was reclaimed when the arena trimmed; only the image block for 1x1 _empty remains.
        Assert.Equal(1, gpu.MemoryAllocator.PooledBlockCount);
    }

    [VulkanFact]
    public void LargeTextureUploadUsesOneShotStagingWithoutGrowingArena()
    {
        using var gpu = VulkanGpuDevice.Create(new VulkanDeviceOptions { Api = VulkanTheoryAttribute.Loader() });
        gpu.ReclaimIdleMemory();
        Assert.Equal(0, gpu.ArenaBytes);

        // 512x512 RGBA8 = 1 MiB (>= OneShotStagingThreshold).
        var large = new byte[512 * 512 * 4];
        using (var texture = gpu.CreateTexture(512, 512, large, GpuFilter.Linear))
        {
            Assert.Equal(0, gpu.ArenaBytes);
        }

        gpu.ReclaimIdleMemory();
        Assert.Equal(0, gpu.PendingGarbageCount);
        Assert.Equal(0, gpu.ArenaBytes);
    }

    [VulkanFact]
    public unsafe void ExternalRenderTargetAllocatesDepthStencilLazily()
    {
        using var gpu = VulkanGpuDevice.Create(new VulkanDeviceOptions { Api = VulkanTheoryAttribute.Loader() });
        var image = gpu.CreateImage(64, 64, VulkanGpuDevice.ColorFormat, 1, SampleCountFlags.Count1Bit,
            ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            out var allocation);
        try
        {
            using var target = gpu.WrapImage(image, VulkanGpuDevice.ColorFormat, 64, 64, ImageLayout.TransferSrcOptimal);
            Assert.False(target.HasDepthStencil);

            // Color-only clear must not allocate a depth/stencil attachment.
            gpu.BeginFrame(target);
            gpu.Clear(EffectColor.Transparent, stencil: false);
            gpu.EndFrame();
            Assert.False(target.HasDepthStencil);

            // Requesting stencil clear lazily creates depth/stencil on the external target.
            gpu.BeginFrame(target);
            gpu.Clear(EffectColor.Transparent, stencil: true);
            gpu.EndFrame();
            Assert.True(target.HasDepthStencil);

            gpu.ReclaimIdleMemory();
        }
        finally
        {
            gpu.ReclaimIdleMemory();
            gpu.Vk.DestroyImage(gpu.Handle, image, null);
            gpu.MemoryAllocator.Free(allocation);
        }
    }
}

/// <summary>Fact counterpart to <see cref="VulkanTheoryAttribute"/>.</summary>
public sealed class VulkanFactAttribute : FactAttribute
{
    private static readonly string[] Candidates =
        ["/opt/homebrew/lib/libvulkan.1.dylib", "/usr/local/lib/libvulkan.1.dylib"];

    public VulkanFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) Skip = "Requires macOS Vulkan loader.";
        else if (Path() is null) Skip = "No Vulkan loader (install molten-vk and vulkan-loader).";
    }

    private static string? Path() =>
        Environment.GetEnvironmentVariable("VULKAN_LOADER") is { Length: > 0 } custom ? custom : Candidates.FirstOrDefault(File.Exists);
}
