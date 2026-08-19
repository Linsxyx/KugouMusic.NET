using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ATL;
using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using KugouAvaloniaPlayer.Converters;

namespace KugouAvaloniaPlayer.Services;

public sealed class BoundedDiskCachedWebImageLoader(
    string cacheFolder,
    TimeSpan diskCacheLifetime,
    int maxMemoryEntries = BoundedDiskCachedWebImageLoader.DefaultMaxMemoryEntries,
    long maxMemoryBytes = BoundedDiskCachedWebImageLoader.DefaultMaxMemoryBytes,
    long maxDiskBytes = BoundedDiskCachedWebImageLoader.DefaultMaxDiskBytes)
    : BaseWebImageLoader
{
    private const int DefaultMaxMemoryEntries = 200;
    private const long DefaultMaxMemoryBytes = 32L * 1024 * 1024;
    private const long DefaultMaxDiskBytes = 256L * 1024 * 1024;
    private const int EmbeddedCoverDecodeWidth = 128;
    private const int DiskMaintenanceWriteThreshold = 64;
    private const long DiskMaintenanceByteThreshold = 16L * 1024 * 1024;
    private static readonly TimeSpan DiskMaintenanceMinInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DiskMaintenanceMaxInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DiskCacheTouchInterval = TimeSpan.FromHours(1);

    private readonly int _maxMemoryEntries = Math.Max(1, maxMemoryEntries);
    private readonly long _maxMemoryBytes = Math.Max(1, maxMemoryBytes);
    private readonly long _maxDiskBytes = Math.Max(1, maxDiskBytes);
    private readonly Lock _sync = new();
    private readonly Dictionary<string, CacheEntry> _memoryCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WeakReference<Bitmap>> _embeddedBitmapCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<Bitmap?>> _pendingEmbeddedLoads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<byte[]?>> _pendingLoads = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _leastRecentlyUsed = new();
    private readonly CancellationTokenSource _diskMaintenanceCancellation = new();

    private bool _disposed;
    private long _memoryBytes;
    private int _diskWritesSinceMaintenance;
    private long _diskBytesWrittenSinceMaintenance;
    private long _lastDiskMaintenanceTick;
    private int _diskMaintenanceHasRun;
    private int _diskMaintenanceRunning;
    private int _diskMaintenanceStopped;


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

            if (DateTimeOffset.UtcNow - fileInfo.LastWriteTimeUtc > diskCacheLifetime)
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

            Directory.CreateDirectory(cacheFolder);
            await File.WriteAllBytesAsync(GetCachePath(url), bytes).ConfigureAwait(false);
            RecordDiskCacheWrite(bytes.LongLength);
        }
        catch
        {
            // Disk cache is an optimization; image display should not depend on it.
        }
    }

    private string GetCachePath(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Path.Combine(cacheFolder, Convert.ToHexString(hash).ToLowerInvariant());
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

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch
        {
            // Best effort cleanup only.
            return false;
        }
    }

    private void RecordDiskCacheWrite(long byteCount)
    {
        Interlocked.Increment(ref _diskWritesSinceMaintenance);
        Interlocked.Add(ref _diskBytesWrittenSinceMaintenance, byteCount);
        TryScheduleDiskMaintenance();
    }

    private void TryScheduleDiskMaintenance()
    {
        if (Volatile.Read(ref _diskMaintenanceStopped) != 0)
            return;

        var hasRun = Volatile.Read(ref _diskMaintenanceHasRun) != 0;
        var writeCount = Volatile.Read(ref _diskWritesSinceMaintenance);
        if (hasRun && writeCount == 0)
            return;

        var now = Environment.TickCount64;
        var elapsedMilliseconds = hasRun
            ? Math.Max(0, now - Volatile.Read(ref _lastDiskMaintenanceTick))
            : long.MaxValue;
        var thresholdReached = writeCount >= DiskMaintenanceWriteThreshold ||
                               Interlocked.Read(ref _diskBytesWrittenSinceMaintenance) >= DiskMaintenanceByteThreshold;
        var maximumIntervalReached = hasRun &&
                                     elapsedMilliseconds >= (long)DiskMaintenanceMaxInterval.TotalMilliseconds;

        if (hasRun && !thresholdReached && !maximumIntervalReached)
            return;

        var delayMilliseconds = hasRun
            ? Math.Max(0, (long)DiskMaintenanceMinInterval.TotalMilliseconds - elapsedMilliseconds)
            : 0;

        if (Interlocked.CompareExchange(ref _diskMaintenanceRunning, 1, 0) != 0)
            return;

        _ = Task.Run(() => RunScheduledDiskMaintenanceAsync(delayMilliseconds));
    }

    private async Task RunScheduledDiskMaintenanceAsync(long delayMilliseconds)
    {
        try
        {
            var cancellationToken = _diskMaintenanceCancellation.Token;
            if (delayMilliseconds > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(delayMilliseconds), cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _diskWritesSinceMaintenance, 0);
            Interlocked.Exchange(ref _diskBytesWrittenSinceMaintenance, 0);
            TrimDiskCache(cancellationToken);
            Volatile.Write(ref _lastDiskMaintenanceTick, Environment.TickCount64);
            Volatile.Write(ref _diskMaintenanceHasRun, 1);
        }
        catch (OperationCanceledException)
        {
            // Loader disposal cancels pending maintenance.
        }
        catch
        {
            // Disk cache maintenance is best effort only.
        }
        finally
        {
            Volatile.Write(ref _diskMaintenanceRunning, 0);
            TryScheduleDiskMaintenance();
        }
    }

    private void TrimDiskCache(CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(cacheFolder))
                return;

            var totalBytes = MeasureDiskCacheAndDeleteExpiredFiles(cancellationToken);
            if (totalBytes <= _maxDiskBytes)
                return;

            var files = CollectDiskCacheFiles(cancellationToken, out totalBytes);
            if (totalBytes <= _maxDiskBytes)
                return;

            files.Sort(static (left, right) => left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc));
            var lowWatermarkBytes = _maxDiskBytes - Math.Max(1, _maxDiskBytes / 10);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (totalBytes <= lowWatermarkBytes)
                    break;

                if (TryDelete(Path.Combine(cacheFolder, file.FileName)))
                    totalBytes -= file.Length;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private long MeasureDiskCacheAndDeleteExpiredFiles(CancellationToken cancellationToken)
    {
        var totalBytes = 0L;
        var expirationThresholdUtc = DateTime.UtcNow - diskCacheLifetime;

        foreach (var file in EnumerateDiskCacheFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.LastWriteTimeUtc < expirationThresholdUtc &&
                TryDelete(Path.Combine(cacheFolder, file.FileName)))
                continue;

            totalBytes += file.Length;
        }

        return totalBytes;
    }

    private List<DiskCacheFile> CollectDiskCacheFiles(
        CancellationToken cancellationToken,
        out long totalBytes)
    {
        var files = new List<DiskCacheFile>();
        totalBytes = 0;

        foreach (var file in EnumerateDiskCacheFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(file);
            totalBytes += file.Length;
        }

        return files;
    }

    private FileSystemEnumerable<DiskCacheFile> EnumerateDiskCacheFiles()
    {
        var files = new FileSystemEnumerable<DiskCacheFile>(
            cacheFolder,
            static (ref entry) => new DiskCacheFile(
                entry.FileName.ToString(),
                entry.Length,
                entry.LastWriteTimeUtc.UtcDateTime))
        {
            ShouldIncludePredicate = static (ref entry) => !entry.IsDirectory
        };
        return files;
    }

    private static void TouchCacheFile(FileInfo fileInfo)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (now - fileInfo.LastWriteTimeUtc >= DiskCacheTouchInterval)
                fileInfo.LastWriteTimeUtc = now;
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
            Volatile.Write(ref _diskMaintenanceStopped, 1);
            _diskMaintenanceCancellation.Cancel();

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

    private readonly record struct DiskCacheFile(string FileName, long Length, DateTime LastWriteTimeUtc);
}
