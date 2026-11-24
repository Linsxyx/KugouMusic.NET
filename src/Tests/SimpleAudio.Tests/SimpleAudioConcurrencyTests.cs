using System.Diagnostics;
using ManagedBass;
using Xunit;

namespace SimpleAudio.Tests;

// 验证 SimpleAudioPlayer 的锁粒度与可中止加载：慢速流打开不会占用全局 BassDeviceGate，且 Stop 能立即打断正在打开中的流。
[Collection("BASS")]
public sealed class SimpleAudioConcurrencyTests
{
    [Fact]
    public async Task StopOnAnotherPlayer_IsNotBlockedWhileStreamOpenIsWaiting()
    {
        SimpleAudioPlayer.Initialize(0);
        try
        {
            using var playerA = new SimpleAudioPlayer();
            using var playerB = new SimpleAudioPlayer();
            using var blockingStream = new BlockingAbortableStream();

            var loadTask = Task.Run(() => playerA.Load(blockingStream, 65536, "test://blocking.mp3", blockingStream, useUnbuffered: true));

            Assert.True(blockingStream.WaitUntilReadEntered(TimeSpan.FromSeconds(5)), "未能进入慢速流读取回调。");

            var sw = Stopwatch.StartNew();
            playerB.Stop();
            sw.Stop();

            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"另一个播放器 Stop 被慢速 Load 阻塞了 {sw.Elapsed.TotalMilliseconds:F0}ms");

            // 停止 playerA 应立刻触发 Abort 并解除后台 Load。
            var stopSw = Stopwatch.StartNew();
            playerA.Stop();
            var loaded = await loadTask.WaitAsync(TimeSpan.FromSeconds(3));
            stopSw.Stop();

            Assert.False(loaded);
            Assert.True(blockingStream.WasAborted);
            Assert.True(stopSw.Elapsed < TimeSpan.FromSeconds(1), $"中止进行中的 Load 耗时过长: {stopSw.Elapsed.TotalMilliseconds:F0}ms");
            Assert.True(playerA.IsStopped);
        }
        finally
        {
            SimpleAudioPlayer.Free();
            if (Bass.GetDeviceInfo(0, out var noSoundInfo) && noSoundInfo.IsInitialized)
            {
                Bass.CurrentDevice = 0;
                Bass.Free();
            }
        }
    }

    private sealed class BlockingAbortableStream : Stream, IAbortableStreamSource
    {
        private readonly ManualResetEventSlim _readEntered = new(false);
        private readonly ManualResetEventSlim _aborted = new(false);
        private long _position;

        public bool WasAborted => _aborted.IsSet;

        public override bool CanRead => !_aborted.IsSet;
        public override bool CanSeek => !_aborted.IsSet;
        public override bool CanWrite => false;
        public override long Length => 65536;
        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, Length);
        }

        public bool WaitUntilReadEntered(TimeSpan timeout) => _readEntered.Wait(timeout);

        public void Abort() => _aborted.Set();

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            _readEntered.Set();
            _aborted.Wait(TimeSpan.FromSeconds(15));
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            _position = Math.Clamp(target, 0, Length);
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _aborted.Set();
                _readEntered.Dispose();
                _aborted.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
