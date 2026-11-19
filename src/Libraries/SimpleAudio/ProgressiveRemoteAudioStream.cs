using System.Buffers;
using System.Net;
using System.Net.Http.Headers;

namespace SimpleAudio;

// 远程音频播放源与磁盘支撑的渐进式读取流：支持边下边播、未下载完成时的尾部/远距离 HTTP Range 按需读取，以及持续直播流的零落盘小块缓冲模式。

public sealed class RemoteAudioPlaybackSource : IDisposable
{
    private int _disposed;
    private bool _detached;

    private RemoteAudioPlaybackSource(
        string sourceDescription,
        string? localFilePath,
        Stream? stream,
        long contentLength,
        bool useUnbufferedStream,
        bool isLiveStream,
        IDisposable? owner)
    {
        SourceDescription = sourceDescription;
        LocalFilePath = localFilePath;
        Stream = stream;
        ContentLength = contentLength;
        UseUnbufferedStream = useUnbufferedStream;
        IsLiveStream = isLiveStream;
        Owner = owner;
    }

    public string SourceDescription { get; }

    public string? LocalFilePath { get; }

    public Stream? Stream { get; }

    public long ContentLength { get; }

    public bool UseUnbufferedStream { get; }

    public bool IsLiveStream { get; }

    internal IDisposable? Owner { get; }

    public static RemoteAudioPlaybackSource FromLocalFile(string filePath)
    {
        return new RemoteAudioPlaybackSource(
            sourceDescription: filePath,
            localFilePath: filePath,
            stream: null,
            contentLength: 0,
            useUnbufferedStream: false,
            isLiveStream: false,
            owner: null);
    }

    internal static RemoteAudioPlaybackSource FromProgressiveStream(
        string originalUrl,
        ProgressiveRemoteAudioStream stream)
    {
        return new RemoteAudioPlaybackSource(
            sourceDescription: originalUrl,
            localFilePath: null,
            stream: stream,
            contentLength: stream.Length,
            useUnbufferedStream: true,
            isLiveStream: false,
            owner: stream);
    }

    public static RemoteAudioPlaybackSource FromLiveStream(
        string originalUrl,
        Stream networkStream,
        IDisposable? owner)
    {
        return new RemoteAudioPlaybackSource(
            sourceDescription: originalUrl,
            localFilePath: null,
            stream: networkStream,
            contentLength: 0,
            useUnbufferedStream: false,
            isLiveStream: true,
            owner: owner);
    }

    internal void MarkDetached()
    {
        _detached = true;
    }

    public void Dispose()
    {
        if (_detached || Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            Stream?.Dispose();
        }
        catch
        {
            // Ignore disposal errors.
        }

        if (!ReferenceEquals(Owner, Stream))
        {
            try
            {
                Owner?.Dispose();
            }
            catch
            {
                // Ignore disposal errors.
            }
        }
    }
}

// 负责单个有限远程音频文件的后台顺序落盘、进度广播与引用计数生命周期管理。
internal sealed class ProgressiveDownloadSession
{
    private const int CopyBufferSize = 64 * 1024;
    private const int InitialReadyBytes = 32 * 1024;

    private readonly object _sync = new();
    private readonly Action<ProgressiveDownloadSession, bool> _onFinished;
    private readonly CancellationTokenSource _downloadCts = new();
    private readonly TaskCompletionSource<bool> _initialReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string?> _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long _initialThreshold;

    private HttpResponseMessage? _response;
    private Stream? _responseStream;
    private FileStream? _writeStream;
    private long _writtenBytes;
    private int _activeLeases;
    private int _supportsRange;
    private bool _isCompleted;
    private bool _isFailed;
    private bool _isStarted;

    public ProgressiveDownloadSession(
        string cacheKey,
        string url,
        string filePath,
        long totalLength,
        bool supportsRange,
        HttpClient httpClient,
        HttpResponseMessage response,
        Stream responseStream,
        Action<ProgressiveDownloadSession, bool> onFinished)
    {
        CacheKey = cacheKey;
        Url = url;
        FilePath = filePath;
        TotalLength = totalLength;
        HttpClient = httpClient;
        _supportsRange = supportsRange ? 1 : 0;
        _response = response;
        _responseStream = responseStream;
        _onFinished = onFinished;
        _initialThreshold = Math.Min(totalLength, InitialReadyBytes);
        _activeLeases = 1;

        _writeStream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public string CacheKey { get; }

    public string Url { get; }

    public string FilePath { get; }

    public long TotalLength { get; }

    public HttpClient HttpClient { get; }

    public bool SupportsRange => Volatile.Read(ref _supportsRange) != 0;

    public long WrittenBytes
    {
        get
        {
            lock (_sync)
            {
                return _writtenBytes;
            }
        }
    }

    public bool IsCompleted
    {
        get
        {
            lock (_sync)
            {
                return _isCompleted;
            }
        }
    }

    public bool IsFailed
    {
        get
        {
            lock (_sync)
            {
                return _isFailed;
            }
        }
    }

    public void DisableRange()
    {
        Volatile.Write(ref _supportsRange, 0);
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_isStarted)
            {
                return;
            }

            _isStarted = true;
        }

        _ = Task.Run(RunDownloadLoopAsync);
    }

    // 仅在尚未 Start()（如并发 TryAdd 竞争失败）时同步清理已打开的响应与临时文件句柄。
    public void DisposeBeforeStart()
    {
        lock (_sync)
        {
            _isFailed = true;
            _activeLeases = 0;
        }

        try
        {
            _writeStream?.Dispose();
            _writeStream = null;
        }
        catch
        {
        }

        try
        {
            _responseStream?.Dispose();
            _responseStream = null;
        }
        catch
        {
        }

        try
        {
            _response?.Dispose();
            _response = null;
        }
        catch
        {
        }

        _downloadCts.Dispose();
    }

    public bool TryAcquireLease()
    {
        lock (_sync)
        {
            if (_isFailed || (_activeLeases == 0 && !_isCompleted))
            {
                return false;
            }

            _activeLeases++;
            return true;
        }
    }

    public void ReleaseLease()
    {
        var shouldCancel = false;
        lock (_sync)
        {
            if (_activeLeases > 0)
            {
                _activeLeases--;
            }

            if (_activeLeases == 0 && !_isCompleted && !_isFailed)
            {
                shouldCancel = true;
            }
        }

        if (shouldCancel)
        {
            CancelDownload();
        }
    }

    public bool HasActiveLeases
    {
        get
        {
            lock (_sync)
            {
                return _activeLeases > 0;
            }
        }
    }

    public async Task<bool> WaitForInitialDataAsync(CancellationToken cancellationToken)
    {
        return await _initialReadyTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        return await _completionTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // 同步等待写入端推进到至少 requiredBytes，或直到下载完成/失败/调用方中止。
    public bool WaitForBytesAvailable(long requiredBytes, CancellationToken abortToken, TimeSpan timeout)
    {
        var deadlineTicks = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        lock (_sync)
        {
            while (_writtenBytes < requiredBytes && !_isCompleted && !_isFailed)
            {
                if (abortToken.IsCancellationRequested)
                {
                    return false;
                }

                var remainingMs = deadlineTicks - Environment.TickCount64;
                if (remainingMs <= 0)
                {
                    return false;
                }

                Monitor.Wait(_sync, (int)Math.Min(100, remainingMs));
            }

            return _writtenBytes >= requiredBytes || _isCompleted;
        }
    }

    public void PulseWaiters()
    {
        lock (_sync)
        {
            Monitor.PulseAll(_sync);
        }
    }

    private void CancelDownload()
    {
        try
        {
            _downloadCts.Cancel();
        }
        catch
        {
            // Ignore cancellation races.
        }
    }

    private async Task RunDownloadLoopAsync()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        var succeeded = false;
        try
        {
            var responseStream = _responseStream!;
            var writeStream = _writeStream!;
            var ct = _downloadCts.Token;

            while (true)
            {
                var bytesRead = await responseStream
                    .ReadAsync(buffer.AsMemory(0, CopyBufferSize), ct)
                    .ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    break;
                }

                await writeStream
                    .WriteAsync(buffer.AsMemory(0, bytesRead), ct)
                    .ConfigureAwait(false);
                await writeStream.FlushAsync(ct).ConfigureAwait(false);

                var signalInitialReady = false;
                lock (_sync)
                {
                    _writtenBytes += bytesRead;
                    if (_writtenBytes >= _initialThreshold)
                    {
                        signalInitialReady = true;
                    }

                    Monitor.PulseAll(_sync);
                }

                if (signalInitialReady)
                {
                    _initialReadyTcs.TrySetResult(true);
                }
            }

            await writeStream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            await writeStream.DisposeAsync().ConfigureAwait(false);
            _writeStream = null;

            long finalWritten;
            lock (_sync)
            {
                finalWritten = _writtenBytes;
                if (finalWritten > 0 && finalWritten >= TotalLength)
                {
                    _isCompleted = true;
                    succeeded = true;
                }
                else
                {
                    _isFailed = true;
                }

                Monitor.PulseAll(_sync);
            }

            _initialReadyTcs.TrySetResult(succeeded);
            _completionTcs.TrySetResult(succeeded ? FilePath : null);
        }
        catch
        {
            lock (_sync)
            {
                _isFailed = true;
                Monitor.PulseAll(_sync);
            }

            _initialReadyTcs.TrySetResult(false);
            _completionTcs.TrySetResult(null);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);

            try
            {
                if (_writeStream != null)
                {
                    await _writeStream.DisposeAsync().ConfigureAwait(false);
                    _writeStream = null;
                }
            }
            catch
            {
            }

            try
            {
                if (_responseStream != null)
                {
                    await _responseStream.DisposeAsync().ConfigureAwait(false);
                    _responseStream = null;
                }
            }
            catch
            {
            }

            try
            {
                _response?.Dispose();
                _response = null;
            }
            catch
            {
            }

            _downloadCts.Dispose();
            _onFinished(this, succeeded);
        }
    }
}

// 供 BASS StreamSystem.NoBuffer 使用的渐进式只读流：已落盘区域直接读本地文件，尾部探测与远距离 Seek 按需走 HTTP Range，未就绪区域短暂等待写入推进。
internal sealed class ProgressiveRemoteAudioStream : Stream, IAbortableStreamSource
{
    private const long RangeReadThresholdBytes = 256 * 1024;
    private const long TailProbeMaxBytes = 128 * 1024;
    private static readonly TimeSpan ReadWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RangeRequestTimeout = TimeSpan.FromSeconds(15);

    private readonly ProgressiveDownloadSession _session;
    private readonly FileStream _readFileStream;
    private readonly CancellationTokenSource _abortCts = new();

    private long _position;
    private int _aborted;
    private int _disposed;

    private byte[]? _tailCache;
    private long _tailCacheStart = -1;
    private HttpResponseMessage? _rangeResponse;
    private Stream? _rangeStream;
    private long _rangePosition = -1;

    public ProgressiveRemoteAudioStream(ProgressiveDownloadSession session)
    {
        _session = session;
        _readFileStream = new FileStream(
            session.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.RandomAccess);
    }

    public override bool CanRead => Volatile.Read(ref _disposed) == 0;

    public override bool CanSeek => Volatile.Read(ref _disposed) == 0;

    public override bool CanWrite => false;

    public override long Length => _session.TotalLength;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public void Abort()
    {
        if (Interlocked.Exchange(ref _aborted, 1) != 0)
        {
            return;
        }

        try
        {
            _abortCts.Cancel();
        }
        catch
        {
        }

        _session.PulseWaiters();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _session.TotalLength + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (target < 0)
        {
            target = 0;
        }
        else if (target > _session.TotalLength)
        {
            target = _session.TotalLength;
        }

        _position = target;
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (Volatile.Read(ref _aborted) != 0 || Volatile.Read(ref _disposed) != 0 || buffer.Length == 0)
        {
            return 0;
        }

        if (_position >= _session.TotalLength)
        {
            return 0;
        }

        var maxCount = (int)Math.Min(buffer.Length, _session.TotalLength - _position);
        if (maxCount <= 0)
        {
            return 0;
        }

        while (Volatile.Read(ref _aborted) == 0 && Volatile.Read(ref _disposed) == 0)
        {
            var written = _session.WrittenBytes;
            if (_position < written)
            {
                CloseRangeStream();
                var available = (int)Math.Min(maxCount, written - _position);
                var readFromFile = ReadFromLocalFile(buffer[..available]);
                if (readFromFile > 0)
                {
                    return readFromFile;
                }
            }

            if (TryReadFromTailCache(buffer[..maxCount], out var cachedRead))
            {
                return cachedRead;
            }

            if (_session.IsCompleted)
            {
                var finalWritten = _session.WrittenBytes;
                if (_position < finalWritten)
                {
                    var available = (int)Math.Min(maxCount, finalWritten - _position);
                    return ReadFromLocalFile(buffer[..available]);
                }

                return 0;
            }

            if (_session.IsFailed)
            {
                return 0;
            }

            var gap = _position - written;
            if (gap > RangeReadThresholdBytes && _session.SupportsRange)
            {
                var rangeRead = TryReadViaRange(buffer[..maxCount]);
                if (rangeRead > 0)
                {
                    return rangeRead;
                }
            }

            var requiredBytes = Math.Min(_session.TotalLength, _position + 1);
            if (!_session.WaitForBytesAvailable(requiredBytes, _abortCts.Token, ReadWaitTimeout))
            {
                return 0;
            }
        }

        return 0;
    }

    private int ReadFromLocalFile(Span<byte> destination)
    {
        if (_readFileStream.Position != _position)
        {
            _readFileStream.Position = _position;
        }

        var read = _readFileStream.Read(destination);
        if (read > 0)
        {
            _position += read;
        }

        return read;
    }

    private bool TryReadFromTailCache(Span<byte> destination, out int bytesRead)
    {
        bytesRead = 0;
        if (_tailCache == null || _tailCacheStart < 0)
        {
            return false;
        }

        var tailEnd = _tailCacheStart + _tailCache.Length;
        if (_position < _tailCacheStart || _position >= tailEnd)
        {
            return false;
        }

        var offset = (int)(_position - _tailCacheStart);
        var count = Math.Min(destination.Length, _tailCache.Length - offset);
        _tailCache.AsSpan(offset, count).CopyTo(destination);
        _position += count;
        bytesRead = count;
        return true;
    }

    private int TryReadViaRange(Span<byte> destination)
    {
        var remainingToEnd = _session.TotalLength - _position;
        if (remainingToEnd <= TailProbeMaxBytes)
        {
            if (TryPopulateTailCache(_position) && TryReadFromTailCache(destination, out var cachedRead))
            {
                return cachedRead;
            }

            return 0;
        }

        try
        {
            if (_rangeStream != null && _rangePosition == _position)
            {
                var read = ReadStreamCancelable(_rangeStream, destination, _abortCts.Token, RangeRequestTimeout);
                if (read > 0)
                {
                    _rangePosition += read;
                    _position += read;
                    return read;
                }

                CloseRangeStream();
            }

            CloseRangeStream();
            using var request = new HttpRequestMessage(HttpMethod.Get, _session.Url);
            request.Headers.Range = new RangeHeaderValue(_position, null);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_abortCts.Token);
            timeoutCts.CancelAfter(RangeRequestTimeout);

            var response = _session.HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .GetAwaiter()
                .GetResult();

            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                response.Dispose();
                _session.DisableRange();
                return 0;
            }

            var stream = response.Content.ReadAsStreamAsync(timeoutCts.Token).GetAwaiter().GetResult();
            _rangeResponse = response;
            _rangeStream = stream;
            _rangePosition = _position;

            var bytesRead = ReadStreamCancelable(stream, destination, _abortCts.Token, RangeRequestTimeout);
            if (bytesRead > 0)
            {
                _rangePosition += bytesRead;
                _position += bytesRead;
                return bytesRead;
            }

            CloseRangeStream();
            return 0;
        }
        catch
        {
            CloseRangeStream();
            return 0;
        }
    }

    private bool TryPopulateTailCache(long startOffset)
    {
        try
        {
            var endOffset = _session.TotalLength - 1;
            if (startOffset > endOffset)
            {
                return false;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, _session.Url);
            request.Headers.Range = new RangeHeaderValue(startOffset, endOffset);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_abortCts.Token);
            timeoutCts.CancelAfter(RangeRequestTimeout);

            using var response = _session.HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .GetAwaiter()
                .GetResult();

            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                _session.DisableRange();
                return false;
            }

            var expectedLength = (int)(endOffset - startOffset + 1);
            var data = new byte[expectedLength];
            using var stream = response.Content.ReadAsStreamAsync(timeoutCts.Token).GetAwaiter().GetResult();
            var totalRead = 0;
            while (totalRead < expectedLength)
            {
                var n = stream.ReadAsync(data.AsMemory(totalRead, expectedLength - totalRead), timeoutCts.Token)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                if (n <= 0)
                {
                    break;
                }

                totalRead += n;
            }

            if (totalRead <= 0)
            {
                return false;
            }

            if (totalRead < expectedLength)
            {
                Array.Resize(ref data, totalRead);
            }

            _tailCacheStart = startOffset;
            _tailCache = data;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int ReadStreamCancelable(
        Stream stream,
        Span<byte> destination,
        CancellationToken abortToken,
        TimeSpan timeout)
    {
        var rented = ArrayPool<byte>.Shared.Rent(destination.Length);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(abortToken);
            timeoutCts.CancelAfter(timeout);
            var read = stream.ReadAsync(rented.AsMemory(0, destination.Length), timeoutCts.Token)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            if (read > 0)
            {
                rented.AsSpan(0, read).CopyTo(destination);
            }

            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private void CloseRangeStream()
    {
        if (_rangeStream != null)
        {
            try
            {
                _rangeStream.Dispose();
            }
            catch
            {
            }

            _rangeStream = null;
        }

        if (_rangeResponse != null)
        {
            try
            {
                _rangeResponse.Dispose();
            }
            catch
            {
            }

            _rangeResponse = null;
        }

        _rangePosition = -1;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        Abort();
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (disposing)
        {
            CloseRangeStream();
            _tailCache = null;
            try
            {
                _readFileStream.Dispose();
            }
            catch
            {
            }

            _abortCts.Dispose();
            _session.ReleaseLease();
        }

        base.Dispose(disposing);
    }
}
