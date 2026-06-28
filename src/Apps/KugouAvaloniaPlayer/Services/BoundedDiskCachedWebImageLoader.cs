using System;
using System.Collections.Generic;
using System.IO;
using ZLinq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ATL;
using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using KugouAvaloniaPlayer.Converters;

namespace KugouAvaloniaPlayer.Services;

public sealed class BoundedDiskCachedWebImageLoader : BaseWebImageLoader
{
    private const int DefaultMaxMemoryEntries = 200;
    private const long DefaultMaxMemoryBytes = 32L * 1024 * 1024;
    private const long DefaultMaxDiskBytes = 256L * 1024 * 1024;
    private const int EmbeddedCoverDecodeWidth = 128;

    private readonly string _cacheFolder;
    private readonly TimeSpan _diskCacheLifetime;
    private readonly int _maxMemoryEntries;
    private readonly long _maxMemoryBytes;
    private readonly long _maxDiskBytes;
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _memoryCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WeakReference<Bitmap>> _embeddedBitmapCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<Bitmap?>> _pendingEmbeddedLoads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<byte[]?>> _pendingLoads = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _leastRecentlyUsed = new();

    private bool _disposed;
    private long _memoryBytes;

    public BoundedDiskCachedWebImageLoader(
        string cacheFolder,
        TimeSpan diskCacheLifetime,
        int maxMemoryEntries = DefaultMaxMemoryEntries,
        long maxMemoryBytes = DefaultMaxMemoryBytes,
        long maxDiskBytes = DefaultMaxDiskBytes)
    {
        _cacheFolder = cacheFolder;
        _diskCacheLifetime = diskCacheLifetime;
        _maxMemoryEntries = Math.Max(1, maxMemoryEntries);
        _maxMemoryBytes = Math.Max(1, maxMemoryBytes);
        _maxDiskBytes = Math.Max(1, maxDiskBytes);
    }

    public BoundedDiskCachedWebImageLoader(
        HttpClient httpClient,
        bool disposeHttpClient,
        string cacheFolder,
        TimeSpan diskCacheLifetime,
        int maxMemoryEntries = DefaultMaxMemoryEntries,
        long maxMemoryBytes = DefaultMaxMemoryBytes,
        long maxDiskBytes = DefaultMaxDiskBytes)
        : base(httpClient, disposeHttpClient)
    {
        _cacheFolder = cacheFolder;
        _diskCacheLifetime = diskCacheLifetime;
        _maxMemoryEntries = Math.Max(1, maxMemoryEntries);
        _maxMemoryBytes = Math.Max(1, maxMemoryBytes);
        _maxDiskBytes = Math.Max(1, maxDiskBytes);
    }

    public override async Task<Bitmap?> ProvideImageAsync(string url)
    {
        return await ProvideImageAsync(url, null).ConfigureAwait(false);
    }

    public override async Task<Bitmap?> ProvideImageAsync(string url, IStorageProvider? storageProvider = null)
    {
        if (LocalImageSourceHelper.TryGetEmbeddedCoverFilePath(url, out var embeddedTrackPath))
            return await GetEmbeddedCoverBitmapAsync(url, embeddedTrackPath!).ConfigureAwait(false);

        if (!IsWebUrl(url))
            return await base.ProvideImageAsync(url, storageProvider).ConfigureAwait(false);

        var bytes = await GetExternalImageBytesAsync(url).ConfigureAwait(false);
        if (bytes is not { Length: > 0 })
            return null;

        using var stream = new MemoryStream(bytes, writable: false);
        return new Bitmap(stream);
    }

    private async Task<Bitmap?> GetEmbeddedCoverBitmapAsync(string sourceKey, string trackPath)
    {
        if (TryGetEmbeddedBitmapFromMemory(sourceKey, out var cachedBitmap))
            return cachedBitmap;

        var loadTask = GetOrCreateEmbeddedLoadTask(sourceKey, trackPath);
        try
        {
            return await loadTask.ConfigureAwait(false);
        }
        finally
        {
            RemovePendingEmbeddedLoad(sourceKey, loadTask);
        }
    }

    private bool TryGetEmbeddedBitmapFromMemory(string sourceKey, out Bitmap? bitmap)
    {
        lock (_sync)
        {
            if (_embeddedBitmapCache.TryGetValue(sourceKey, out var weakReference) &&
                weakReference.TryGetTarget(out bitmap))
            {
                return true;
            }

            bitmap = null;
            return false;
        }
    }

    private Task<Bitmap?> GetOrCreateEmbeddedLoadTask(string sourceKey, string trackPath)
    {
        lock (_sync)
        {
            if (_pendingEmbeddedLoads.TryGetValue(sourceKey, out var existingTask))
                return existingTask;

            var loadTask = LoadEmbeddedCoverBitmapAsync(sourceKey, trackPath);
            _pendingEmbeddedLoads[sourceKey] = loadTask;
            return loadTask;
        }
    }

    private async Task<Bitmap?> LoadEmbeddedCoverBitmapAsync(string sourceKey, string trackPath)
    {
        if (TryGetEmbeddedBitmapFromMemory(sourceKey, out var cachedBitmap))
            return cachedBitmap;

        var bitmap = await Task.Run(() =>
        {
            try
            {
                var track = new Track(trackPath);
                var picture = track.EmbeddedPictures.Count > 0 ? track.EmbeddedPictures[0] : null;
                if (picture?.PictureData == null || picture.PictureData.Length == 0)
                    return null;

                using var stream = new MemoryStream(picture.PictureData, writable: false);
                return Bitmap.DecodeToWidth(stream, EmbeddedCoverDecodeWidth, BitmapInterpolationMode.LowQuality);
            }
            catch
            {
                return null;
            }
        }).ConfigureAwait(false);

        if (bitmap == null)
            return null;

        lock (_sync)
        {
            _embeddedBitmapCache[sourceKey] = new WeakReference<Bitmap>(bitmap);
        }

        return bitmap;
    }

    private async Task<byte[]?> GetExternalImageBytesAsync(string url)
    {
        if (TryGetFromMemory(url, out var cachedBytes))
            return cachedBytes;

        var loadTask = GetOrCreateLoadTask(url);

        try
        {
            return await loadTask.ConfigureAwait(false);
        }
        finally
        {
            RemovePendingLoad(url, loadTask);
        }
    }

    private Task<byte[]?> GetOrCreateLoadTask(string url)
    {
        lock (_sync)
        {
            if (_pendingLoads.TryGetValue(url, out var existingTask))
                return existingTask;

            var loadTask = LoadAndCacheExternalBytesAsync(url);
            _pendingLoads[url] = loadTask;
            return loadTask;
        }
    }

    private async Task<byte[]?> LoadAndCacheExternalBytesAsync(string url)
    {
        if (TryGetFromMemory(url, out var cachedBytes))
            return cachedBytes;

        var diskBytes = await TryReadDiskCacheAsync(url).ConfigureAwait(false);
        if (diskBytes is { Length: > 0 })
        {
            AddToMemoryCache(url, diskBytes);
            return diskBytes;
        }

        var downloadedBytes = await LoadDataFromExternalAsync(url).ConfigureAwait(false);
        if (downloadedBytes is not { Length: > 0 })
            return null;

        await TryWriteDiskCacheAsync(url, downloadedBytes).ConfigureAwait(false);
        AddToMemoryCache(url, downloadedBytes);
        return downloadedBytes;
    }

    private bool TryGetFromMemory(string url, out byte[]? bytes)
    {
        lock (_sync)
        {
            if (!_memoryCache.TryGetValue(url, out var entry))
            {
                bytes = null;
                return false;
            }

            _leastRecentlyUsed.Remove(entry.Node);
            _leastRecentlyUsed.AddFirst(entry.Node);
            bytes = entry.Bytes;
            return true;
        }
    }

    private void AddToMemoryCache(string url, byte[] bytes)
    {
        if (bytes.LongLength > _maxMemoryBytes)
            return;

        lock (_sync)
        {
            if (_disposed)
                return;

            if (_memoryCache.TryGetValue(url, out var existing))
            {
                _memoryBytes -= existing.Size;
                _leastRecentlyUsed.Remove(existing.Node);
            }

            var node = new LinkedListNode<string>(url);
            _leastRecentlyUsed.AddFirst(node);
            _memoryCache[url] = new CacheEntry(bytes, bytes.LongLength, node);
            _memoryBytes += bytes.LongLength;

            TrimMemoryCache();
        }
    }

    private void TrimMemoryCache()
    {
        while (_memoryCache.Count > _maxMemoryEntries || _memoryBytes > _maxMemoryBytes)
        {
            var node = _leastRecentlyUsed.Last;
            if (node is null)
                return;

            if (_memoryCache.Remove(node.Value, out var removed))
                _memoryBytes -= removed.Size;

            _leastRecentlyUsed.RemoveLast();
        }
    }

    private async Task<byte[]?> TryReadDiskCacheAsync(string url)
    {
        try
        {
            var cachePath = GetCachePath(url);
            var fileInfo = new FileInfo(cachePath);
            if (!fileInfo.Exists)
                return null;

            if (DateTimeOffset.UtcNow - fileInfo.LastWriteTimeUtc > _diskCacheLifetime)
            {
                TryDelete(fileInfo.FullName);
                return null;
            }

            TouchCacheFile(fileInfo);
            return await File.ReadAllBytesAsync(fileInfo.FullName).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task TryWriteDiskCacheAsync(string url, byte[] bytes)
    {
        try
        {
            if (bytes.LongLength > _maxDiskBytes)
                return;

            Directory.CreateDirectory(_cacheFolder);
            await File.WriteAllBytesAsync(GetCachePath(url), bytes).ConfigureAwait(false);
            TrimDiskCache();
        }
        catch
        {
            // Disk cache is an optimization; image display should not depend on it.
        }
    }

    private string GetCachePath(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Path.Combine(_cacheFolder, Convert.ToHexString(hash).ToLowerInvariant());
    }

    private void RemovePendingLoad(string url, Task<byte[]?> loadTask)
    {
        lock (_sync)
        {
            if (_pendingLoads.TryGetValue(url, out var currentTask) && ReferenceEquals(currentTask, loadTask))
                _pendingLoads.Remove(url);
        }
    }

    private void RemovePendingEmbeddedLoad(string sourceKey, Task<Bitmap?> loadTask)
    {
        lock (_sync)
        {
            if (_pendingEmbeddedLoads.TryGetValue(sourceKey, out var currentTask) && ReferenceEquals(currentTask, loadTask))
                _pendingEmbeddedLoads.Remove(sourceKey);
        }
    }

    private static bool IsWebUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private void TrimDiskCache()
    {
        try
        {
            var directory = new DirectoryInfo(_cacheFolder);
            if (!directory.Exists)
                return;

            var files = directory
                .EnumerateFiles()
                .AsValueEnumerable().OrderByDescending(static x => x.LastWriteTimeUtc)
                .ToList();
            var totalBytes = files.AsValueEnumerable().Sum(static x => x.Length);
            if (totalBytes <= _maxDiskBytes)
                return;

            foreach (var file in files.AsValueEnumerable().Reverse())
            {
                if (totalBytes <= _maxDiskBytes)
                    break;

                var length = file.Length;
                TryDelete(file.FullName);
                totalBytes -= length;
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static void TouchCacheFile(FileInfo fileInfo)
    {
        try
        {
            fileInfo.LastWriteTimeUtc = DateTime.UtcNow;
        }
        catch
        {
            // Best effort recency update only.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_sync)
            {
                _disposed = true;
                _memoryCache.Clear();
                _pendingLoads.Clear();
                _leastRecentlyUsed.Clear();
                _memoryBytes = 0;
            }
        }

        base.Dispose(disposing);
    }

    private sealed record CacheEntry(byte[] Bytes, long Size, LinkedListNode<string> Node);
}
