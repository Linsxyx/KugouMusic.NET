using ManagedBass;

namespace SimpleAudio;

internal sealed class ManagedStreamSource(
    Stream inputStream,
    long contentLength,
    IDisposable? owner = null) : IDisposable
{
    private int _disposed;

    public FileProcedures CreateProcedures()
    {
        return new FileProcedures
        {
            Close = _ => Dispose(),
            Length = _ => GetLength(),
            Read = Read,
            Seek = Seek
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        inputStream.Dispose();
        if (!ReferenceEquals(owner, inputStream))
            owner?.Dispose();
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
        if (Volatile.Read(ref _disposed) != 0 || length <= 0)
            return 0;

        try
        {
            // BASS owns this memory for the duration of the synchronous callback.
            // Do not retain the span or use it across an asynchronous operation.
            return inputStream.Read(new Span<byte>(buffer.ToPointer(), length));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException)
        {
            if (Volatile.Read(ref _disposed) == 0)
                Console.WriteLine($"[BASS Managed Stream Read Error] {ex.Message}");
            return 0;
        }
    }

    private bool Seek(long offset, IntPtr user)
    {
        if (Volatile.Read(ref _disposed) != 0 || !inputStream.CanSeek)
            return false;

        try
        {
            inputStream.Position = offset;
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
