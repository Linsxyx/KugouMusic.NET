using System.Collections.Concurrent;
using SkiaSharp;

namespace KaMusic.Cli.Graphics;

// 封面 / 头像 / 二维码的异步下载、解码与内存缓存。

internal sealed class ImageCache : IDisposable
{
    private const int MaxDecodedSize = 640;
    private const int MaxEntries = 48;

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, TerminalImage> _images = new();
    private readonly ConcurrentDictionary<string, byte> _loading = new();
    private readonly ConcurrentDictionary<string, byte> _failed = new();
    private readonly Queue<string> _order = new();
    private readonly Action _onLoaded;

    public ImageCache(Action onLoaded)
    {
        _onLoaded = onLoaded;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 KgTest/1.0");
    }

    /// <summary>
    ///     获取已加载的图片；未加载时在后台开始下载并返回 null，完成后触发重绘。
    /// </summary>
    public TerminalImage? Get(string? url)
    {
        var normalized = NormalizeUrl(url);
        if (normalized == null)
        {
            return null;
        }

        if (_images.TryGetValue(normalized, out var image))
        {
            return image;
        }

        if (_failed.ContainsKey(normalized) || !_loading.TryAdd(normalized, 0))
        {
            return null;
        }

        _ = Task.Run(() => LoadAsync(normalized));
        return null;
    }

    /// <summary>
    ///     从 base64（可带 data:image/...;base64, 前缀）同步解码图片。
    /// </summary>
    public TerminalImage? FromBase64(string key, string? base64, bool crisp)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        if (_images.TryGetValue(key, out var existing))
        {
            return existing;
        }

        try
        {
            var comma = base64.IndexOf(',');
            var payload = comma >= 0 ? base64[(comma + 1)..] : base64;
            var bytes = Convert.FromBase64String(payload.Trim());
            var bitmap = SKBitmap.Decode(bytes);
            if (bitmap == null)
            {
                return null;
            }

            var image = new TerminalImage(key, bitmap, crisp);
            Store(key, image);
            return image;
        }
        catch
        {
            return null;
        }
    }

    public static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var value = url.Trim().Replace("{size}", "400");
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            value = "https:" + value;
        }

        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    private async Task LoadAsync(string url)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            var bitmap = SKBitmap.Decode(bytes);
            if (bitmap == null)
            {
                _failed.TryAdd(url, 0);
                return;
            }

            if (bitmap.Width > MaxDecodedSize || bitmap.Height > MaxDecodedSize)
            {
                var scale = MaxDecodedSize / (double)Math.Max(bitmap.Width, bitmap.Height);
                var resized = bitmap.Resize(
                    new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (resized != null)
                {
                    bitmap.Dispose();
                    bitmap = resized;
                }
            }

            Store(url, new TerminalImage(url, bitmap, false));
            _onLoaded();
        }
        catch
        {
            _failed.TryAdd(url, 0);
        }
        finally
        {
            _loading.TryRemove(url, out _);
        }
    }

    private void Store(string key, TerminalImage image)
    {
        lock (_order)
        {
            if (!_images.TryAdd(key, image))
            {
                image.Dispose();
                return;
            }

            _order.Enqueue(key);
            // 只做淘汰不做 Dispose：正在渲染的帧可能仍持有旧引用，交给 GC 回收。
            while (_order.Count > MaxEntries && _order.TryDequeue(out var oldest))
            {
                _images.TryRemove(oldest, out _);
            }
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
