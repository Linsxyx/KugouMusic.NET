using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SimpleAudio;

namespace KaMusic.Cli.Tests;

// 验证远程音频渐进式缓存与分流：慢速点播文件不等待全量下载即返回、未下完时支持 Range 尾部读取、切歌及时取消、无限持续流可立即播放且不被分析管线阻塞。

public sealed class RemoteAudioStreamingTests
{
    [Fact]
    public async Task OpenPlaybackSourceAsync_SlowFiniteFile_StartsBeforeFullDownloadAndCachesForReuse()
    {
        var wavData = CreateSyntheticWav(durationSeconds: 3);
        var fullRequests = 0;
        var rangeRequests = 0;
        var downloadFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = new LocalTestHttpServer(async (requestLine, headers, stream, ct) =>
        {
            if (headers.TryGetValue("Range", out var rangeValue) && TryParseRange(rangeValue, wavData.Length, out var start, out var end))
            {
                Interlocked.Increment(ref rangeRequests);
                var sliceLength = end - start + 1;
                var headerText =
                    $"HTTP/1.1 206 Partial Content\r\n" +
                    $"Content-Type: audio/wav\r\n" +
                    $"Accept-Ranges: bytes\r\n" +
                    $"Content-Range: bytes {start}-{end}/{wavData.Length}\r\n" +
                    $"Content-Length: {sliceLength}\r\n" +
                    $"Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headerText), ct);
                await stream.WriteAsync(wavData.AsMemory((int)start, (int)sliceLength), ct);
                return;
            }

            Interlocked.Increment(ref fullRequests);
            var fullHeader =
                $"HTTP/1.1 200 OK\r\n" +
                $"Content-Type: audio/wav\r\n" +
                $"Accept-Ranges: bytes\r\n" +
                $"Content-Length: {wavData.Length}\r\n" +
                $"Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(fullHeader), ct);

            // 分块慢速吐出数据：总耗时约 2.5s+，验证首次开播远早于下载完成。
            const int chunkSize = 16 * 1024;
            for (var offset = 0; offset < wavData.Length; offset += chunkSize)
            {
                var len = Math.Min(chunkSize, wavData.Length - offset);
                await stream.WriteAsync(wavData.AsMemory(offset, len), ct);
                await stream.FlushAsync(ct);
                if (offset >= 32 * 1024)
                {
                    await Task.Delay(35, ct);
                }
            }

            downloadFinished.TrySetResult(true);
        });

        var url = $"{server.BaseUrl}/slow-{Guid.NewGuid():N}.wav";
        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        var sw = Stopwatch.StartNew();
        using var playbackSource = await RemoteAudioCache.OpenPlaybackSourceAsync(url, httpClient);
        sw.Stop();

        Assert.False(playbackSource.IsLiveStream);
        Assert.True(playbackSource.UseUnbufferedStream);
        Assert.NotNull(playbackSource.Stream);
        Assert.Equal(wavData.Length, playbackSource.ContentLength);
        // 应在整首下载完成之前就返回播放源。
        Assert.False(downloadFinished.Task.IsCompleted);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1.5), $"期望首块即返回，实际耗时 {sw.Elapsed.TotalMilliseconds:F0}ms");

        // 下载尚未完成时，直接 Seek 到末尾读取最后 128 字节（模拟 BASS 读取 ID3v1/尾部标签）应通过 Range 快速完成。
        var tailSw = Stopwatch.StartNew();
        playbackSource.Stream!.Seek(wavData.Length - 128, SeekOrigin.Begin);
        var tailBuffer = new byte[128];
        var tailRead = playbackSource.Stream.Read(tailBuffer, 0, tailBuffer.Length);
        tailSw.Stop();

        Assert.Equal(128, tailRead);
        Assert.Equal(wavData[^128..], tailBuffer);
        Assert.True(Volatile.Read(ref rangeRequests) >= 1);
        Assert.True(tailSw.Elapsed < TimeSpan.FromSeconds(1.0));

        // 后台分析复用同一个下载会话，等待完成后不发起第二次完整下载。
        var completedPath = await RemoteAudioCache.ResolveCompletedFileAsync(url, httpClient);
        Assert.False(string.IsNullOrWhiteSpace(completedPath));
        Assert.True(File.Exists(completedPath));
        Assert.Equal(wavData.Length, new FileInfo(completedPath!).Length);
        Assert.Equal(1, Volatile.Read(ref fullRequests));

        // 再次打开同一 URL 直接命中本地完整缓存，不再发 HTTP 请求。
        using var secondSource = await RemoteAudioCache.OpenPlaybackSourceAsync(url, httpClient);
        Assert.Equal(completedPath, secondSource.LocalFilePath);
        Assert.Null(secondSource.Stream);
        Assert.Equal(1, Volatile.Read(ref fullRequests));
    }

    [Fact]
    public async Task OpenPlaybackSourceAsync_InfiniteLiveStream_ReturnsImmediatelyAndSkipsFullAnalysis()
    {
        var wavHeader = CreateSyntheticWav(durationSeconds: 1)[..44];
        var pcmChunk = new byte[8192];
        var clientDisconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = new LocalTestHttpServer(async (_, _, stream, ct) =>
        {
            var header =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: audio/wav\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
            await WriteChunkAsync(stream, wavHeader, ct);

            try
            {
                // 持续流永不发送 0 长度结束块，直到客户端断开。
                while (!ct.IsCancellationRequested)
                {
                    await WriteChunkAsync(stream, pcmChunk, ct);
                    await Task.Delay(50, ct);
                }
            }
            catch
            {
                clientDisconnected.TrySetResult(true);
            }
        });

        var url = $"{server.BaseUrl}/live-{Guid.NewGuid():N}.wav";
        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        var sw = Stopwatch.StartNew();
        using var playbackSource = await RemoteAudioCache.OpenPlaybackSourceAsync(url, httpClient);
        sw.Stop();

        Assert.True(playbackSource.IsLiveStream);
        Assert.False(playbackSource.UseUnbufferedStream);
        Assert.Equal(0, playbackSource.ContentLength);
        Assert.NotNull(playbackSource.Stream);
        Assert.True(RemoteAudioCache.IsKnownLiveStream(url));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1.5), $"持续流应立即返回，实际耗时 {sw.Elapsed.TotalMilliseconds:F0}ms");

        // 持续流应立即跳过全量文件解析、响度测量与过渡分析，不得阻塞。
        Assert.Null(await RemoteAudioCache.ResolveCompletedFileAsync(url, httpClient));
        Assert.Equal(1.0f, await TrackVolumeNormalizer.EstimateGainAsync(url, isLocal: false, durationSeconds: 0));
        var analyzer = new ManagedBassTransitionAnalysisService();
        var profile = await analyzer.AnalyzeAsync(
            new PreparedTrack { Id = "live-a", Source = url, IsLocal = false, DurationSeconds = 0 },
            new PreparedTrack { Id = "live-b", Source = url, IsLocal = false, DurationSeconds = 0 });
        Assert.NotNull(profile);

        // 读取首块数据确认流可读，然后释放确认可立刻关闭连接。
        var probe = new byte[44];
        var read = await playbackSource.Stream!.ReadAsync(probe);
        Assert.True(read > 0);

        playbackSource.Dispose();
        // 默认 SocketsHttpHandler 带有 2 秒的 ResponseDrainTimeout，因此留足 4 秒观察服务端断连。
        var disconnected = await Task.WhenAny(clientDisconnected.Task, Task.Delay(4000));
        Assert.Equal(clientDisconnected.Task, disconnected);
    }

    [Fact]
    public async Task ProgressiveStream_DisposeBeforeCompletion_CancelsBackgroundDownload()
    {
        var wavData = CreateSyntheticWav(durationSeconds: 4);
        var bytesServed = 0;
        var serverCanceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = new LocalTestHttpServer(async (_, _, stream, ct) =>
        {
            var header =
                $"HTTP/1.1 200 OK\r\n" +
                $"Content-Type: audio/wav\r\n" +
                $"Accept-Ranges: bytes\r\n" +
                $"Content-Length: {wavData.Length}\r\n" +
                $"Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);

            const int chunkSize = 16 * 1024;
            try
            {
                for (var offset = 0; offset < wavData.Length; offset += chunkSize)
                {
                    var len = Math.Min(chunkSize, wavData.Length - offset);
                    await stream.WriteAsync(wavData.AsMemory(offset, len), ct);
                    await stream.FlushAsync(ct);
                    Interlocked.Add(ref bytesServed, len);
                    if (offset >= 32 * 1024)
                    {
                        await Task.Delay(60, ct);
                    }
                }
            }
            catch
            {
                serverCanceled.TrySetResult(true);
            }
        });

        var url = $"{server.BaseUrl}/abort-{Guid.NewGuid():N}.wav";
        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        using (var playbackSource = await RemoteAudioCache.OpenPlaybackSourceAsync(url, httpClient))
        {
            Assert.True(playbackSource.UseUnbufferedStream);
        }

        // 释放未下载完的流后，后台下载应很快终止，且不会进入已完成缓存。
        await Task.WhenAny(serverCanceled.Task, Task.Delay(2000));
        Assert.True(Volatile.Read(ref bytesServed) < wavData.Length);
        Assert.False(RemoteAudioCache.TryGetCachedFilePath(url, out _));
    }

    [Fact]
    public async Task DualTrackAudioPlayer_LoadsProgressiveAndLiveStreamsWhenOutputDeviceAvailable()
    {
        SimpleAudioPlayer.Initialize();
        if (SimpleAudioPlayer.GetOutputDevices().Count <= 1)
        {
            return;
        }

        var wavData = CreateSyntheticWav(durationSeconds: 2);
        await using var server = new LocalTestHttpServer(async (requestLine, headers, stream, ct) =>
        {
            if (requestLine.Contains("/live-", StringComparison.Ordinal))
            {
                var liveHeader =
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: audio/wav\r\n" +
                    "Transfer-Encoding: chunked\r\n" +
                    "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(liveHeader), ct);
                await WriteChunkAsync(stream, wavData[..44], ct);
                var chunk = new byte[16 * 1024];
                while (!ct.IsCancellationRequested)
                {
                    await WriteChunkAsync(stream, chunk, ct);
                    await Task.Delay(30, ct);
                }

                return;
            }

            if (headers.TryGetValue("Range", out var rangeValue) && TryParseRange(rangeValue, wavData.Length, out var start, out var end))
            {
                var sliceLength = end - start + 1;
                var partialHeader =
                    $"HTTP/1.1 206 Partial Content\r\n" +
                    $"Content-Type: audio/wav\r\n" +
                    $"Accept-Ranges: bytes\r\n" +
                    $"Content-Range: bytes {start}-{end}/{wavData.Length}\r\n" +
                    $"Content-Length: {sliceLength}\r\n" +
                    $"Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(partialHeader), ct);
                await stream.WriteAsync(wavData.AsMemory((int)start, (int)sliceLength), ct);
                return;
            }

            var header =
                $"HTTP/1.1 200 OK\r\n" +
                $"Content-Type: audio/wav\r\n" +
                $"Accept-Ranges: bytes\r\n" +
                $"Content-Length: {wavData.Length}\r\n" +
                $"Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
            const int chunkSize = 16 * 1024;
            for (var offset = 0; offset < wavData.Length; offset += chunkSize)
            {
                var len = Math.Min(chunkSize, wavData.Length - offset);
                await stream.WriteAsync(wavData.AsMemory(offset, len), ct);
                await stream.FlushAsync(ct);
                if (offset >= 32 * 1024)
                {
                    await Task.Delay(25, ct);
                }
            }
        });

        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var player = new DualTrackAudioPlayer();

        var progressiveUrl = $"{server.BaseUrl}/bass-prog-{Guid.NewGuid():N}.wav";
        using var progressiveSource = await RemoteAudioCache.OpenPlaybackSourceAsync(progressiveUrl, httpClient);
        Assert.True(player.Load(progressiveSource), player.LastErrorDetail);
        Assert.False(player.IsActiveLiveStream);
        Assert.True(player.GetDuration() > TimeSpan.FromSeconds(1.5));
        player.Stop();

        var liveUrl = $"{server.BaseUrl}/live-{Guid.NewGuid():N}.wav";
        using var liveSource = await RemoteAudioCache.OpenPlaybackSourceAsync(liveUrl, httpClient);
        Assert.True(player.Load(liveSource), player.LastErrorDetail);
        Assert.True(player.IsActiveLiveStream);

        var stopSw = Stopwatch.StartNew();
        player.Stop();
        stopSw.Stop();
        Assert.True(stopSw.Elapsed < TimeSpan.FromSeconds(1.5), $"停止持续流不应卡顿，实际耗时 {stopSw.Elapsed.TotalMilliseconds:F0}ms");
    }

    private static byte[] CreateSyntheticWav(int durationSeconds)
    {
        const int sampleRate = 44100;
        const short channels = 2;
        const short bitsPerSample = 16;
        var byteRate = sampleRate * channels * (bitsPerSample / 8);
        var dataSize = byteRate * durationSeconds;
        var wav = new byte[44 + dataSize];

        "RIFF"u8.CopyTo(wav.AsSpan(0));
        BitConverter.TryWriteBytes(wav.AsSpan(4), 36 + dataSize);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BitConverter.TryWriteBytes(wav.AsSpan(16), 16);
        BitConverter.TryWriteBytes(wav.AsSpan(20), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(22), channels);
        BitConverter.TryWriteBytes(wav.AsSpan(24), sampleRate);
        BitConverter.TryWriteBytes(wav.AsSpan(28), byteRate);
        BitConverter.TryWriteBytes(wav.AsSpan(32), (short)(channels * (bitsPerSample / 8)));
        BitConverter.TryWriteBytes(wav.AsSpan(34), bitsPerSample);
        "data"u8.CopyTo(wav.AsSpan(36));
        BitConverter.TryWriteBytes(wav.AsSpan(40), dataSize);

        // 填充确定性波形字节以便精确校验 Range 读取内容。
        for (var i = 44; i < wav.Length; i++)
        {
            wav[i] = (byte)((i * 31) & 0xFF);
        }

        return wav;
    }

    private static async Task WriteChunkAsync(Stream stream, byte[] data, CancellationToken ct)
    {
        var prefix = Encoding.ASCII.GetBytes($"{data.Length:X}\r\n");
        await stream.WriteAsync(prefix, ct);
        await stream.WriteAsync(data, ct);
        await stream.WriteAsync("\r\n"u8.ToArray(), ct);
        await stream.FlushAsync(ct);
    }

    private static bool TryParseRange(string headerValue, int totalLength, out long start, out long end)
    {
        start = 0;
        end = totalLength - 1;
        if (!headerValue.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = headerValue["bytes=".Length..].Split('-', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out start))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(parts[1]) && long.TryParse(parts[1], out var parsedEnd))
        {
            end = Math.Min(parsedEnd, totalLength - 1);
        }

        return start >= 0 && start <= end;
    }

    // 轻量本地 TCP HTTP 服务器，支持精细控制慢速分块、Range 与永不结束的 chunked 持续流。
    private sealed class LocalTestHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<string, Dictionary<string, string>, NetworkStream, CancellationToken, Task> _handler;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public LocalTestHttpServer(Func<string, Dictionary<string, string>, NetworkStream, CancellationToken, Task> handler)
        {
            _handler = handler;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUrl = $"http://127.0.0.1:{port}";
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public string BaseUrl { get; }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client));
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                try
                {
                    var requestHeader = await ReadHttpHeaderAsync(stream, _cts.Token);
                    if (string.IsNullOrEmpty(requestHeader))
                    {
                        return;
                    }

                    var lines = requestHeader.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                    var requestLine = lines[0];
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 1; i < lines.Length; i++)
                    {
                        var colon = lines[i].IndexOf(':');
                        if (colon > 0)
                        {
                            headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
                        }
                    }

                    await _handler(requestLine, headers, stream, _cts.Token);
                }
                catch
                {
                    // Test server connection closed or canceled.
                }
            }
        }

        private static async Task<string> ReadHttpHeaderAsync(NetworkStream stream, CancellationToken ct)
        {
            var sb = new StringBuilder();
            var buffer = new byte[1024];
            while (sb.Length < 16384)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read <= 0)
                {
                    break;
                }

                sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
                var text = sb.ToString();
                var marker = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (marker >= 0)
                {
                    return text[..marker];
                }
            }

            return string.Empty;
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try
            {
                await _acceptLoop;
            }
            catch
            {
            }

            _cts.Dispose();
        }
    }
}
