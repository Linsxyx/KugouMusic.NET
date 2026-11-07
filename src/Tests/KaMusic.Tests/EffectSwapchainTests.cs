using Avalonia;
using AvaloniaSilkEffects.Gpu;
using AvaloniaSilkEffects.Gpu.Presentation;

namespace AvaloniaSilkEffects.Tests;

public sealed class EffectSwapchainTests
{
    private static readonly PixelSize Size = new(3420, 1994);

    [Fact]
    public void SlowCompositorCannotGrowTheSwapchainOrOverwritePendingImages()
    {
        var created = new List<Image>();
        var pool = new EffectSwapchain(size => { var image = new Image(size); created.Add(image); return image; });
        for (var i = 0; i < EffectSwapchain.MaximumImages; i++)
            Assert.IsType<Image>(pool.Acquire(Size)).Pending();

        for (var i = 0; i < 10000; i++) Assert.Null(pool.Acquire(Size));
        Assert.Equal(EffectSwapchain.MaximumImages, created.Count);
        Assert.All(created, image => Assert.False(image.Disposed));

        created[0].Complete();
        Assert.Same(created[0], pool.Acquire(Size));
        Assert.Equal(EffectSwapchain.MaximumImages, created.Count);
    }

    [Fact]
    public void NormalPresentationKeepsAReadySpareAndReusesImages()
    {
        var created = 0;
        var pool = new EffectSwapchain(size => { created++; return new Image(size); });
        for (var i = 0; i < 1000; i++)
        {
            var image = Assert.IsType<Image>(pool.Acquire(Size));
            image.Pending();
            image.Complete();
        }
        Assert.Equal(2, created);
        Assert.Equal(2, pool.Count);
    }

    [Fact]
    public void ReusedImagesKeepThePreviousPresentationOrder()
    {
        var pool = new EffectSwapchain(size => new Image(size));
        var images = Enumerable.Range(0, EffectSwapchain.MaximumImages).Select(_ =>
        {
            var image = Assert.IsType<Image>(pool.Acquire(Size));
            image.Pending();
            return image;
        }).ToArray();
        images[0].Complete();
        Assert.Same(images[0], pool.Acquire(Size));
        images[0].Pending();
        foreach (var image in images) image.Complete();
        Assert.Same(images[0], pool.Acquire(Size));
    }

    [Fact]
    public void ResizeWaitsForOldImagesBeforeReleasingAndReplacingThem()
    {
        var pool = new EffectSwapchain(size => new Image(size));
        var old = Enumerable.Range(0, EffectSwapchain.MaximumImages).Select(_ =>
        {
            var image = Assert.IsType<Image>(pool.Acquire(Size));
            image.Pending();
            return image;
        }).ToArray();
        var resized = new PixelSize(1280, 720);
        Assert.Null(pool.Acquire(resized));
        old[1].Complete();
        Assert.Equal(resized, pool.Acquire(resized)!.Size);
        Assert.True(old[1].Disposed);
        Assert.False(old[0].Disposed);
        Assert.False(old[2].Disposed);
        Assert.Equal(EffectSwapchain.MaximumImages, pool.Count);
    }

    [Fact]
    public void FailedAndCancelledPresentationsAreReclaimed()
    {
        var pool = new EffectSwapchain(size => new Image(size));
        var faulted = Assert.IsType<Image>(pool.Acquire(Size));
        faulted.Fail();
        var cancelled = Assert.IsType<Image>(pool.Acquire(Size));
        Assert.True(faulted.Disposed);
        cancelled.Cancel();
        Assert.NotSame(cancelled, pool.Acquire(Size));
        Assert.True(cancelled.Disposed);
    }

    [Fact]
    public void ShutdownAlsoOwnsAnImageWhoseDrawOrPresentFailed()
    {
        var pool = new EffectSwapchain(size => new Image(size));
        var drawing = Assert.IsType<Image>(pool.Acquire(Size));
        drawing.Pending();
        pool.Release(force: false);
        Assert.False(drawing.Disposed);
        Assert.Equal(1, pool.Count);
        pool.Release(force: true);
        pool.Release(force: true);
        Assert.True(drawing.Disposed);
        Assert.Equal(0, pool.Count);
    }

    private sealed class Image(PixelSize size) : SwapchainImage(size)
    {
        private TaskCompletionSource? _completion;
        public bool Disposed { get; private set; }
        public void Pending() { _completion = new(); LastPresent = _completion.Task; }
        public void Complete() => _completion!.SetResult();
        public void Fail() => LastPresent = Task.FromException(new InvalidOperationException("compositor lost"));
        public void Cancel() => LastPresent = Task.FromCanceled(new CancellationToken(true));
        public override GpuRenderTarget Target => throw new NotSupportedException();
        public override void BeginDraw() { }
        public override void Present(bool drawn) { }
        public override void Dispose() => Disposed = true;
    }
}
