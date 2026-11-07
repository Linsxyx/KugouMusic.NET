using AvaloniaSilkEffects.Gpu.OpenGl;
using System.Numerics;
using SkiaSharp;

namespace AvaloniaSilkEffects.Tests;

public sealed class TextureCacheOpenGlTests
{
    [MacOpenGlFact]
    public void IdleCollectionIsBatchedAndFinishesOnFollowingFrames() => TemperaOpenGlTests.WithGl(gl =>
    {
        using var gpu = new GlGpuDevice(gl);
        using var cache = new EffectTextureCache(gpu);
        var textures = Enumerable.Range(0, 48).Select(_ => cache.CreateRgba(new byte[16], 2, 2)).ToArray();
        for (var i = 0; i < 32; i++) cache.BeginFrame();
        cache.Collect(0);
        Assert.Equal(48 - EffectTextureCache.CollectionBatchSize, cache.Count);
        Assert.Equal(EffectTextureCache.CollectionBatchSize, textures.Count(t => t.IsDisposed));
        cache.BeginFrame(); cache.Collect(0);
        Assert.Equal(16, cache.Count);
        cache.BeginFrame(); cache.Collect(0);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.ResidentBytes);
    });

    [MacOpenGlFact]
    public void SoftPressureUsesLruWhileSeverePressureDrainsToLimit() => TemperaOpenGlTests.WithGl(gl =>
    {
        using var gpu = new GlGpuDevice(gl);
        using var cache = new EffectTextureCache(gpu) { ResidentTextureLimit = 80 };
        var textures = Enumerable.Range(0, 100).Select(_ => cache.CreateRgba(new byte[4], 1, 1)).ToArray();
        cache.BeginFrame();
        cache.Touch(textures[0]);
        cache.Collect();
        Assert.Equal(100 - EffectTextureCache.CollectionBatchSize, cache.Count);
        Assert.False(textures[0].IsDisposed);
        Assert.True(textures[1].IsDisposed);
        cache.BeginFrame(); cache.Collect();
        Assert.Equal(80, cache.Count);

        cache.ResidentTextureLimit = 10;
        cache.BeginFrame();
        cache.Collect();
        Assert.Equal(10, cache.Count);
    });

    [MacOpenGlFact]
    public void MemoryPressureNeverEvictsTexturesUsedByTheCurrentDraws() => TemperaOpenGlTests.WithGl(gl =>
    {
        using var gpu = new GlGpuDevice(gl);
        using var cache = new EffectTextureCache(gpu) { ResidentByteLimit = 1 };
        var active = cache.CreateRgba(new byte[64], 4, 4);
        cache.Collect(0);
        Assert.False(active.IsDisposed);
        cache.BeginFrame(); cache.Touch(active); cache.Collect(0);
        Assert.False(active.IsDisposed);
        cache.BeginFrame(); cache.Collect(0);
        Assert.True(active.IsDisposed);
        Assert.Equal(0, cache.ResidentBytes);
    });

    [MacOpenGlFact]
    public void ReleasingEveryKeyKindAllowsRecreationAndAccountsActualStorage() => TemperaOpenGlTests.WithGl(gl =>
    {
        using var gpu = new GlGpuDevice(gl);
        using var cache = new EffectTextureCache(gpu);
        EffectTexture Text() => cache.GetOrCreateText("歌词", "PingFang SC", 24, 600, EffectColor.White);
        EffectTexture Layer() => cache.GetOrCreateTextLayer("发光", "PingFang SC", 24, 600, EffectColor.White, EffectColor.White, 2);
        EffectTexture Vector() => cache.GetOrCreateVector("vector", new Vector2(8, 4), 1, c => c.Clear(SKColors.White));
        foreach (var create in new Func<EffectTexture>[] { Text, Layer, Vector })
        {
            var original = create();
            Assert.Same(original, create());
            cache.Release(original);
            cache.Release(original);
            Assert.True(original.IsDisposed);
            var replacement = create();
            Assert.NotSame(original, replacement);
            Assert.False(replacement.IsDisposed);
            cache.Release(replacement);
        }
        var rgba = cache.CreateRgba(new byte[128], 8, 4);
        Assert.Equal(128, cache.ResidentBytes);
        cache.Release(rgba);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.ResidentBytes);
    });
}
