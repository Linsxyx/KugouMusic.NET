using ManagedBass;
using System.Runtime.InteropServices;
using SimpleAudio.Codecs.Av3a;

namespace SimpleAudio;

internal sealed class ManagedStreamSource(
    Stream inputStream,
    long contentLength,
    IDisposable? owner = null) : IDisposable
{
    // Must remain strongly reachable for the entire native stream lifetime.
    private FileProcedures? _procedures;
    private GCHandle _root;
    private int _disposed;
    private int _cancelled;
    public Action? Closed { get; set; }

    public void CancelPendingRead()
    {
        if (Interlocked.Exchange(ref _cancelled, 1) != 0 || Volatile.Read(ref _disposed) != 0)
            return;
        try
        {
            if (inputStream is Av3aWaveStream wave)
                wave.CancelPendingRead();
            else
                inputStream.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Closing an in-flight HTTP read can race with normal stream shutdown.
        }
    }

    public FileProcedures CreateProcedures()
    {
        if (_procedures != null)
            return _procedures;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _root = GCHandle.Alloc(this);
        return _procedures = new FileProcedures
        {
            Close = _ =>
            {
                try { Dispose(); }
                catch (Exception ex) { Console.WriteLine($"[BASS Managed Stream Close Error] {ex.Message}"); }
            },
            Length = _ => GetLength(),
            Read = Read,
            Seek = Seek
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            inputStream.Dispose();
        }
        finally
        {
            try
            {
                if (!ReferenceEquals(owner, inputStream))
                    owner?.Dispose();
            }
            finally
            {
                if (_root.IsAllocated)
                    _root.Free();
                Closed?.Invoke();
            }
        }
    }

    private long GetLength()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return 0;
        if (contentLength > 0)
            return contentLength;

        if (inputStream is SeekableReadCache cache)
            return cache.KnownLength;

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
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _cancelled) != 0 || length <= 0)
            return 0;

        try
        {
            return inputStream.Read(new Span<byte>(buffer.ToPointer(), length));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException)
        {
            if (Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _cancelled) == 0)
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
