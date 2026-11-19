using ManagedBass;

namespace SimpleAudio;

// 允许在 Bass.StreamFree 之前提前中断阻塞中的读回调，避免弱网或断流切歌时释放流卡死。
internal interface IAbortableStreamSource
{
    void Abort();
}

internal sealed class ManagedStreamSource(
    Stream inputStream,
    long contentLength,
    IDisposable? owner = null) : IDisposable, IAbortableStreamSource
{
    // Must remain strongly reachable for the entire native stream lifetime.
    private FileProcedures? _procedures;
    private int _aborted;
    private int _disposed;

    public FileProcedures CreateProcedures()
    {
        return _procedures ??= new FileProcedures
        {
            Close = _ => { },
            Length = _ => GetLength(),
            Read = Read,
            Seek = Seek
        };
    }

    public void Abort()
    {
        if (Interlocked.Exchange(ref _aborted, 1) != 0)
            return;

        if (inputStream is IAbortableStreamSource abortableStream)
        {
            abortableStream.Abort();
        }
        else if (owner is IAbortableStreamSource abortableOwner)
        {
            abortableOwner.Abort();
        }
        else
        {
            try
            {
                inputStream.Dispose();
            }
            catch
            {
                // Ignore best-effort abort errors.
            }
        }
    }

    public void Dispose()
    {
        Abort();
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            inputStream.Dispose();
        }
        catch
        {
            // Ignore disposal errors after abort.
        }

        if (!ReferenceEquals(owner, inputStream))
        {
            try
            {
                owner?.Dispose();
            }
            catch
            {
                // Ignore disposal errors after abort.
            }
        }
    }

    private long GetLength()
    {
        if (contentLength > 0)
            return contentLength;

        if (!inputStream.CanSeek)
            return 0;

        try
        {
            return inputStream.Length;
        }
        catch (NotSupportedException)
        {
            return 0;
        }
    }

    private unsafe int Read(IntPtr buffer, int length, IntPtr user)
    {
        if (Volatile.Read(ref _aborted) != 0 || Volatile.Read(ref _disposed) != 0 || length <= 0)
            return 0;

        try
        {
            return inputStream.Read(new Span<byte>(buffer.ToPointer(), length));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException or OperationCanceledException)
        {
            if (Volatile.Read(ref _aborted) == 0 && Volatile.Read(ref _disposed) == 0)
                Console.WriteLine($"[BASS Managed Stream Read Error] {ex.Message}");
            return 0;
        }
    }

    private bool Seek(long offset, IntPtr user)
    {
        if (Volatile.Read(ref _aborted) != 0 || Volatile.Read(ref _disposed) != 0 || !inputStream.CanSeek)
            return false;

        try
        {
            inputStream.Position = offset;
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or ObjectDisposedException)
        {
            return false;
        }
    }
}
