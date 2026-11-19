using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SimpleAudio;

// 远程音频渐进式缓存与分流器：
// - 有限点播文件：收到响应头与首块数据即返回磁盘支撑的渐进流（StreamSystem.NoBuffer），后台单连接顺序落盘并共享给预加载、过渡分析与响度测量。
// - 持续/未知长度流：不落盘、不等结束，直接返回流式源（StreamSystem.Buffer + StreamDownloadBlocks）并标记跳过全量分析。

public static class RemoteAudioCache
{
    private const int MaxCachedFiles = 6;
    private const long MaxCachedBytes = 256L * 1024 * 1024;
    private const int MaxKnownLiveStreamEntries = 128;

    internal static readonly string CacheDirectory = Path.Combine(
        ResolveCacheRoot(),
        "kugou",
        "simple-audio-cache",
        "remote-streams");

    private static readonly HttpClient DefaultHttpClient = CreateDefaultHttpClient();
    private static readonly ConcurrentDictionary<string, ProgressiveDownloadSession> Inflight = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CompletedCacheEntry> CompletedFiles = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> KnownLiveStreams = new(StringComparer.Ordinal);
    private static readonly Lock TrimGate = new();
    private static int _cleanupRegistered;

    // 打开远程音频播放源：已缓存直接返回本地文件；未缓存有限文件收到首块即返回渐进流；未知长度或超大流直接返回持续流。
    public static async Task<RemoteAudioPlaybackSource> OpenPlaybackSourceAsync(
        string source,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source) || !IsHttpUrl(source))
        {
            return RemoteAudioPlaybackSource.FromLocalFile(source);
        }

        EnsureCleanupRegistered();

        if (TryGetCachedFilePath(source, out var cachedPath))
        {
            return RemoteAudioPlaybackSource.FromLocalFile(cachedPath);
        }

        var cacheKey = ComputeCacheKey(source);
        var client = httpClient ?? DefaultHttpClient;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetCachedFilePath(source, out cachedPath))
            {
                return RemoteAudioPlaybackSource.FromLocalFile(cachedPath);
            }

            if (Inflight.TryGetValue(cacheKey, out var existingSession) && existingSession.TryAcquireLease())
            {
                try
                {
                    var ready = await existingSession.WaitForInitialDataAsync(cancellationToken).ConfigureAwait(false);
                    if (ready && File.Exists(existingSession.FilePath))
                    {
                        var stream = new ProgressiveRemoteAudioStream(existingSession);
                        return RemoteAudioPlaybackSource.FromProgressiveStream(source, stream);
                    }

                    existingSession.ReleaseLease();
                }
                catch
                {
                    existingSession.ReleaseLease();
                    throw;
                }
            }

            HttpResponseMessage? response = null;
            Stream? responseStream = null;
            ProgressiveDownloadSession? createdSession = null;
            var ownershipTransferred = false;

            try
            {
                response = await client
                    .GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var contentLength = response.Content.Headers.ContentLength;
                responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                // 无 Content-Length 或超过单文件上限视为持续流/超大流：不落盘、不等结束，直接边收边播。
                if (contentLength is not > 0 || contentLength.Value > MaxCachedBytes)
                {
                    MarkKnownLiveStream(cacheKey);
                    ownershipTransferred = true;
                    return RemoteAudioPlaybackSource.FromLiveStream(source, responseStream, response);
                }

                KnownLiveStreams.TryRemove(cacheKey, out _);

                var supportsRange = !response.Headers.AcceptRanges.Any(
                    static value => string.Equals(value, "none", StringComparison.OrdinalIgnoreCase));
                Directory.CreateDirectory(CacheDirectory);
                var filePath = BuildSessionFilePath(source, cacheKey);

                createdSession = new ProgressiveDownloadSession(
                    cacheKey,
                    source,
                    filePath,
                    contentLength.Value,
                    supportsRange,
                    client,
                    response,
                    responseStream,
                    OnSessionFinished);

                // 响应与响应流所有权已移交给 createdSession。
                response = null;
                responseStream = null;

                if (!Inflight.TryAdd(cacheKey, createdSession))
                {
                    // 并发已有其他线程启动了同一首曲目的下载会话，释放本次冗余会话并重试复用。
                    createdSession.DisposeBeforeStart();
                    TryDeleteFile(filePath);
                    createdSession = null;
                    continue;
                }

                createdSession.Start();
                var initialReady = await createdSession.WaitForInitialDataAsync(cancellationToken).ConfigureAwait(false);
                if (!initialReady || !File.Exists(createdSession.FilePath))
                {
                    throw new IOException($"远程音频初始缓冲失败: {source}");
                }

                var progressiveStream = new ProgressiveRemoteAudioStream(createdSession);
                ownershipTransferred = true;
                return RemoteAudioPlaybackSource.FromProgressiveStream(source, progressiveStream);
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    if (createdSession != null)
                    {
                        createdSession.ReleaseLease();
                    }

                    if (responseStream != null)
                    {
                        try
                        {
                            responseStream.Dispose();
                        }
                        catch
                        {
                        }
                    }

                    if (response != null)
                    {
                        try
                        {
                            response.Dispose();
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }
    }

    // 供过渡分析与响度测量复用：等待完整本地缓存文件就绪；若为持续/未知长度流则返回 null，避免对其执行全量解码。
    public static async Task<string?> ResolveCompletedFileAsync(
        string source,
        HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        if (!IsHttpUrl(source))
        {
            return File.Exists(source) ? source : null;
        }

        EnsureCleanupRegistered();

        if (IsKnownLiveStream(source))
        {
            return null;
        }

        if (TryGetCachedFilePath(source, out var cachedPath))
        {
            return cachedPath;
        }

        var cacheKey = ComputeCacheKey(source);
        if (Inflight.TryGetValue(cacheKey, out var existingSession) && existingSession.TryAcquireLease())
        {
            try
            {
                var completedPath = await existingSession.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);
                return !string.IsNullOrWhiteSpace(completedPath) && File.Exists(completedPath)
                    ? completedPath
                    : null;
            }
            finally
            {
                existingSession.ReleaseLease();
            }
        }

        // 若尚未有人开始下载，通过同一套入口探测并下载；持续流会立即返回 IsLiveStream=true 并关闭，不阻塞。
        using var playbackSource = await OpenPlaybackSourceAsync(source, httpClient, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(playbackSource.LocalFilePath) && File.Exists(playbackSource.LocalFilePath))
        {
            return playbackSource.LocalFilePath;
        }

        if (playbackSource.IsLiveStream)
        {
            return null;
        }

        if (Inflight.TryGetValue(cacheKey, out var startedSession) && startedSession.TryAcquireLease())
        {
            try
            {
                var completedPath = await startedSession.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);
                return !string.IsNullOrWhiteSpace(completedPath) && File.Exists(completedPath)
                    ? completedPath
                    : null;
            }
            finally
            {
                startedSession.ReleaseLease();
            }
        }

        return TryGetCachedFilePath(source, out cachedPath) ? cachedPath : null;
    }

    public static bool IsKnownLiveStream(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || !IsHttpUrl(source))
        {
            return false;
        }

        return KnownLiveStreams.ContainsKey(ComputeCacheKey(source));
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

        var cacheKey = ComputeCacheKey(source);
        if (!CompletedFiles.TryGetValue(cacheKey, out var entry))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(entry.FilePath);
            if (!info.Exists || info.Length <= 0)
            {
                CompletedFiles.TryRemove(cacheKey, out _);
                return false;
            }

            entry.Touch();
            TouchFile(info);
            localPath = info.FullName;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // 识别位于临时远程缓存目录下的文件并提取跨进程稳定的 SHA256 键，供持久化分析缓存复用。
    internal static bool TryGetStableCacheKeyForTempFile(string fullPath, out string stableKey)
    {
        stableKey = string.Empty;
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            var cacheDir = Path.GetFullPath(CacheDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrEmpty(directory) ||
                !string.Equals(
                    Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    cacheDir,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var fileName = Path.GetFileNameWithoutExtension(fullPath);
            var separator = fileName.IndexOf('_');
            var candidate = separator > 0 ? fileName[..separator] : fileName;
            if (candidate.Length != 64)
            {
                return false;
            }

            for (var i = 0; i < candidate.Length; i++)
            {
                if (!Uri.IsHexDigit(candidate[i]))
                {
                    return false;
                }
            }

            stableKey = candidate;
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
            CompletedFiles.Clear();
            try
            {
                if (!Directory.Exists(CacheDirectory))
                {
                    return;
                }

                foreach (var file in Directory.EnumerateFiles(CacheDirectory))
                {
                    TryDeleteFile(file);
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    private static void OnSessionFinished(ProgressiveDownloadSession session, bool succeeded)
    {
        if (Inflight.TryGetValue(session.CacheKey, out var current) && ReferenceEquals(current, session))
        {
            Inflight.TryRemove(session.CacheKey, out _);
        }

        if (succeeded && File.Exists(session.FilePath))
        {
            var entry = new CompletedCacheEntry(session.CacheKey, session.FilePath, session.TotalLength);
            CompletedFiles.AddOrUpdate(
                session.CacheKey,
                entry,
                (_, previous) =>
                {
                    if (!string.Equals(previous.FilePath, session.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDeleteFile(previous.FilePath);
                    }

                    return entry;
                });

            TrimCache(excludePath: session.FilePath);
        }
        else if (!session.HasActiveLeases)
        {
            TryDeleteFile(session.FilePath);
        }
    }

    private static void MarkKnownLiveStream(string cacheKey)
    {
        if (KnownLiveStreams.Count > MaxKnownLiveStreamEntries)
        {
            KnownLiveStreams.Clear();
        }

        KnownLiveStreams[cacheKey] = 0;
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

                var activeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var session in Inflight.Values)
                {
                    activeFiles.Add(session.FilePath);
                }

                var completedByPath = new Dictionary<string, CompletedCacheEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in CompletedFiles.Values)
                {
                    completedByPath[entry.FilePath] = entry;
                }

                // 清理已失联的孤立临时文件（如取消后残留的半截文件）。
                foreach (var file in dir.EnumerateFiles())
                {
                    if (!activeFiles.Contains(file.FullName) && !completedByPath.ContainsKey(file.FullName))
                    {
                        TryDeleteFile(file.FullName);
                    }
                }

                var entries = CompletedFiles.Values
                    .OrderByDescending(static e => e.LastAccessedUtcTicks)
                    .ToList();

                var totalBytes = entries.Sum(static e => e.Length);
                if (entries.Count <= MaxCachedFiles && totalBytes <= MaxCachedBytes)
                {
                    return;
                }

                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries.Count <= MaxCachedFiles && totalBytes <= MaxCachedBytes)
                    {
                        break;
                    }

                    var entry = entries[i];
                    if (!string.IsNullOrEmpty(excludePath) &&
                        string.Equals(entry.FilePath, excludePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        if (File.Exists(entry.FilePath))
                        {
                            File.Delete(entry.FilePath);
                        }

                        CompletedFiles.TryRemove(entry.CacheKey, out _);
                        entries.RemoveAt(i);
                        totalBytes -= entry.Length;
                    }
                    catch
                    {
                        // 文件可能正被播放器或分析器占用，保留等待下次清理。
                    }
                }
            }
            catch
            {
                // Best-effort trim.
            }
        }
    }

    private static string BuildSessionFilePath(string url, string cacheKey)
    {
        var ext = ExtractAudioExtension(url);
        return Path.Combine(CacheDirectory, $"{cacheKey}_{Guid.NewGuid():N}{ext}");
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

        // 启动时清理上次进程异常退出可能残留的临时文件。
        CleanupAll();
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => CleanupAll();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxResponseDrainSize = 0,
            ResponseDrainTimeout = TimeSpan.Zero
        };
        var client = new HttpClient(handler)
        {
            // 不设全局固定 Timeout，避免持续音频流或大文件慢速下载在 45s 后被强行掐断；单次请求超时由调用方 CancellationToken 控制。
            Timeout = Timeout.InfiniteTimeSpan
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

    private sealed class CompletedCacheEntry(string cacheKey, string filePath, long length)
    {
        private long _lastAccessedUtcTicks = DateTime.UtcNow.Ticks;

        public string CacheKey { get; } = cacheKey;

        public string FilePath { get; } = filePath;

        public long Length { get; } = length;

        public long LastAccessedUtcTicks => Volatile.Read(ref _lastAccessedUtcTicks);

        public void Touch()
        {
            Volatile.Write(ref _lastAccessedUtcTicks, DateTime.UtcNow.Ticks);
        }
    }
}
