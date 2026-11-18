using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace SimpleAudio;

// 远程音频临时文件缓存：将 HTTP 音频流落盘到固定上限的临时目录，供播放、预加载、过渡分析与响度测量共享同一份本地文件，避免 BASS 在内存中保存多份完整网络缓冲。

public static class RemoteAudioCache
{
    private const int MaxCachedFiles = 6;
    private const long MaxCachedBytes = 256L * 1024 * 1024;
    private const int CopyBufferSize = 81920;

    private static readonly string CacheDirectory = Path.Combine(
        ResolveCacheRoot(),
        "kugou",
        "simple-audio-cache",
        "remote-streams");

    private static readonly HttpClient DefaultHttpClient = CreateDefaultHttpClient();
    private static readonly ConcurrentDictionary<string, InflightDownload> Inflight = new(StringComparer.Ordinal);
    private static readonly Lock TrimGate = new();
    private static int _cleanupRegistered;

    // 解析音频源：若已存在对应远程缓存文件或本身为本地文件则直接返回，否则通过 HTTP 流式下载到临时缓存文件后返回本地路径。
    public static async Task<string> ResolveOrDownloadAsync(
        string source,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source) || !IsHttpUrl(source))
        {
            return source;
        }

        EnsureCleanupRegistered();

        if (TryGetCachedFilePath(source, out var existingPath))
        {
            return existingPath;
        }

        var cacheKey = ComputeCacheKey(source);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetCachedFilePath(source, out existingPath))
            {
                return existingPath;
            }

            var inflight = Inflight.GetOrAdd(
                cacheKey,
                _ => new InflightDownload(source, httpClient ?? DefaultHttpClient));

            try
            {
                var path = await inflight.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return path;
                }

                return source;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                if (Inflight.TryGetValue(cacheKey, out var current) && ReferenceEquals(current, inflight))
                {
                    Inflight.TryRemove(cacheKey, out _);
                }

                return source;
            }
        }
    }

    public static bool TryGetCachedFilePath(string? source, out string localPath)
    {
        localPath = string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (!IsHttpUrl(source))
        {
            if (File.Exists(source))
            {
                localPath = source;
                return true;
            }

            return false;
        }

        try
        {
            var path = GetTargetFilePath(source);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0)
            {
                return false;
            }

            TouchFile(info);
            localPath = info.FullName;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsHttpUrl(string? source)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static void CleanupAll()
    {
        lock (TrimGate)
        {
            try
            {
                if (!Directory.Exists(CacheDirectory))
                {
                    return;
                }

                foreach (var file in Directory.EnumerateFiles(CacheDirectory))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Best-effort cleanup; active stream files on Windows may still be open.
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    private static async Task<string?> DownloadToCacheAsync(string cacheKey, string url, HttpClient client)
    {
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var targetPath = GetTargetFilePath(url, cacheKey);
            if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
            {
                TouchFile(new FileInfo(targetPath));
                return targetPath;
            }

            tempPath = Path.Combine(CacheDirectory, $"{cacheKey}.{Guid.NewGuid():N}.part");
            using var response = await client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (var fileStream = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.Read,
                             CopyBufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await responseStream.CopyToAsync(fileStream, CopyBufferSize).ConfigureAwait(false);
                await fileStream.FlushAsync().ConfigureAwait(false);
            }

            var tempInfo = new FileInfo(tempPath);
            if (!tempInfo.Exists || tempInfo.Length <= 0)
            {
                TryDeleteFile(tempPath);
                return null;
            }

            try
            {
                File.Move(tempPath, targetPath, overwrite: true);
                tempPath = null;
            }
            catch
            {
                if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
                {
                    TryDeleteFile(tempPath);
                    tempPath = null;
                }
                else
                {
                    throw;
                }
            }

            TrimCache(excludePath: targetPath);
            return targetPath;
        }
        finally
        {
            if (tempPath != null)
            {
                TryDeleteFile(tempPath);
            }

            Inflight.TryRemove(cacheKey, out _);
        }
    }

    private static void TrimCache(string? excludePath = null)
    {
        lock (TrimGate)
        {
            try
            {
                var dir = new DirectoryInfo(CacheDirectory);
                if (!dir.Exists)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                foreach (var part in dir.EnumerateFiles("*.part"))
                {
                    if (now - part.LastWriteTimeUtc > TimeSpan.FromMinutes(10))
                    {
                        TryDeleteFile(part.FullName);
                    }
                }

                var files = dir
                    .EnumerateFiles()
                    .Where(static f => !f.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(static f => f.LastWriteTimeUtc)
                    .ToList();

                var totalBytes = files.Sum(static f => f.Length);
                if (files.Count <= MaxCachedFiles && totalBytes <= MaxCachedBytes)
                {
                    return;
                }

                for (var i = files.Count - 1; i >= 0; i--)
                {
                    if (files.Count <= MaxCachedFiles && totalBytes <= MaxCachedBytes)
                    {
                        break;
                    }

                    var file = files[i];
                    if (!string.IsNullOrEmpty(excludePath) &&
                        string.Equals(file.FullName, excludePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var length = file.Length;
                    try
                    {
                        file.Delete();
                        files.RemoveAt(i);
                        totalBytes -= length;
                    }
                    catch
                    {
                        // File is currently in use by an active or standby deck; skip it.
                    }
                }
            }
            catch
            {
                // Best-effort trim.
            }
        }
    }

    private static string GetTargetFilePath(string url, string? cacheKey = null)
    {
        var key = cacheKey ?? ComputeCacheKey(url);
        var ext = ExtractAudioExtension(url);
        return Path.Combine(CacheDirectory, key + ext);
    }

    internal static string ComputeCacheKey(string url)
    {
        var identity = ExtractStableUrlIdentity(url);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(bytes);
    }

    private static string ExtractStableUrlIdentity(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return url.Trim();
        }

        // 酷狗等 CDN 的音频地址路径固定（包含音频文件哈希/ID），但查询参数带临时签名与时间戳；
        // 对 Jellyfin 等依赖查询参数的流地址保留关键参数，仅剥离易变鉴权或普通 CDN 签名。
        if (!string.IsNullOrEmpty(uri.AbsolutePath) &&
            HasRecognizedAudioExtension(uri.AbsolutePath))
        {
            return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}";
        }

        return uri.GetLeftPart(UriPartial.Query);
    }

    private static string ExtractAudioExtension(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var ext = Path.GetExtension(uri.AbsolutePath);
            if (IsSafeExtension(ext))
            {
                return ext.ToLowerInvariant();
            }
        }

        return ".audio";
    }

    private static bool HasRecognizedAudioExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".aac", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".opus", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".wma", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".ape", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeExtension(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext) || ext.Length is < 2 or > 8 || ext[0] != '.')
        {
            return false;
        }

        for (var i = 1; i < ext.Length; i++)
        {
            if (!char.IsLetterOrDigit(ext[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void TouchFile(FileInfo info)
    {
        try
        {
            info.LastWriteTimeUtc = DateTime.UtcNow;
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void EnsureCleanupRegistered()
    {
        if (Interlocked.Exchange(ref _cleanupRegistered, 1) != 0)
        {
            return;
        }

        TrimCache();
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => CleanupAll();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 KaMusic/1.0");
        return client;
    }

    private static string ResolveCacheRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : localAppData;
    }

    private sealed class InflightDownload
    {
        public InflightDownload(string url, HttpClient client)
        {
            var key = ComputeCacheKey(url);
            Task = System.Threading.Tasks.Task.Run(() => DownloadToCacheAsync(key, url, client));
        }

        public Task<string?> Task { get; }
    }
}
