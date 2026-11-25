using Avalonia;

namespace AvaloniaSilkEffects.Gpu.Presentation;

/// <summary>
/// Owns every image, including the one currently being drawn. Keep a ready spare when possible,
/// but apply backpressure rather than allocating more full-size images when the compositor falls behind.
/// </summary>
internal sealed class EffectSwapchain(Func<PixelSize, SwapchainImage> createImage)
{
    internal const int MaximumImages = 3;
    private readonly List<SwapchainImage> _images = [];

    public int Count => _images.Count;

    public SwapchainImage? Acquire(PixelSize size)
    {
        SwapchainImage? found = null;
        var ready = 0;
        for (var index = _images.Count - 1; index >= 0; index--)
        {
            var image = _images[index];
            var completed = image.LastPresent is null || image.LastPresent.IsCompleted;
            if (completed && (image.LastPresent is { IsCompletedSuccessfully: false } || image.Size != size))
            {
                image.Dispose();
                _images.RemoveAt(index);
                continue;
            }
            if (!completed) continue;
            ready++;
            found ??= image;
        }

        if (ready >= 2 || _images.Count >= MaximumImages)
        {
            if (found is not null)
            {
                // Keep the presenter's existing reuse order as images become available.
                _images.Remove(found);
                _images.Add(found);
            }
            return found;
        }

        var created = createImage(size);
        _images.Add(created);
        return created;
    }

    public void Release(bool force)
    {
        for (var index = _images.Count - 1; index >= 0; index--)
        {
            var image = _images[index];
            if (!force && image.LastPresent is { IsCompleted: false }) continue;
            image.Dispose();
            _images.RemoveAt(index);
        }
    }

    public Task? FirstPendingPresent()
    {
        foreach (var image in _images)
            if (image.LastPresent is { IsCompleted: false } pending)
                return pending;
        return null;
    }
}

internal abstract class SwapchainImage(PixelSize size) : IDisposable
{
    public PixelSize Size { get; } = size;
    public Task? LastPresent { get; protected set; }
    public abstract GpuRenderTarget Target { get; }
    /// <summary>Queues the GPU wait for the compositor's release and the signal for this frame.</summary>
    public abstract void BeginDraw();
    public abstract void Present(bool drawn);
    public abstract void Dispose();
}
