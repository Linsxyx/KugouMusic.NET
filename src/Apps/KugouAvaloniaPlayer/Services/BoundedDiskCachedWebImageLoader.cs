using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace KugouAvaloniaPlayer.Services;

public sealed class BoundedDiskCachedWebImageLoader : BaseWebImageLoader
{
    private const int DefaultMaxMemoryEntries = 200;
    private const long DefaultMaxMemoryBytes = 32L * 1024 * 1024;

    private readonly string _cacheFolder;
    private readonly TimeSpan _diskCacheLifetime;
    private readonly int _maxMemoryEntries;
    private readonly long _maxMemoryBytes;
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _memoryCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<byte[]?>> _pendingLoads = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _leastRecentlyUsed = new();

    private bool _disposed;
    private long _memoryBytes;

    public BoundedDiskCachedWebImageLoader(
        string cacheFolder,
        TimeSpan diskCacheLifetime,
        int maxMemoryEntries = DefaultMaxMemoryEntries,
        long maxMemoryBytes = DefaultMaxMemoryBytes)
    {
        _cacheFolder = cacheFolder;
        _diskCacheLifetime = diskCacheLifetime;
        _maxMemoryEntries = Math.Max(1, maxMemoryEntries);
        _maxMemoryBytes = Math.Max(1, maxMemoryBytes);
    }

    public BoundedDiskCachedWebImageLoader(
        HttpClient httpClient,
        bool disposeHttpClient,
        string cacheFolder,
        TimeSpan diskCacheLifetime,
        int maxMemoryEntries = DefaultMaxMemoryEntries,
        long maxMemoryBytes = DefaultMaxMemoryBytes)
        : base(httpClient, disposeHttpClient)
    {
        _cacheFolder = cacheFolder;
        _diskCacheLifetime = diskCacheLifetime;
        _maxMemoryEntries = Math.Max(1, maxMemoryEntries);
        _maxMemoryBytes = Math.Max(1, maxMemoryBytes);
    }

    public override async Task<Bitmap?> ProvideImageAsync(string url)
    {
        return await ProvideImageAsync(url, null).ConfigureAwait(false);
    }

    public override async Task<Bitmap?> ProvideImageAsync(string url, IStorageProvider? storageProvider = null)
    {
        if (!IsWebUrl(url))
            return await base.ProvideImageAsync(url, storageProvider).ConfigureAwait(false);

        var bytes = await GetExternalImageBytesAsync(url).ConfigureAwait(false);
        if (bytes is not { Length: > 0 })
            return null;

        using var stream = new MemoryStream(bytes, writable: false);
        return new Bitmap(stream);
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
            Directory.CreateDirectory(_cacheFolder);
            await File.WriteAllBytesAsync(GetCachePath(url), bytes).ConfigureAwait(false);
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
