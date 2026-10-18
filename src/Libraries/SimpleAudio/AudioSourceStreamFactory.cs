using ManagedBass;
using SimpleAudio.Codecs.Av3a;
using System.Collections.Concurrent;

namespace SimpleAudio;

internal static class AudioSourceStreamFactory
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly AsyncLocal<string?> Error = new();
    private static readonly ConcurrentDictionary<int, ManagedStreamSource> ManagedSources = new();
    public static string? LastError => Error.Value;

    public static void CancelPendingReads(int handle)
    {
        if (ManagedSources.TryGetValue(handle, out var source))
            source.CancelPendingRead();
    }

    public static int Create(string source, BassFlags flags)
    {
        Error.Value = null;
        if (File.Exists(source))
        {
            var handle = Bass.CreateStream(source, 0, 0, flags);
            if (handle != 0)
                return handle;
            try
            {
                return Create(File.OpenRead(source), new FileInfo(source).Length, flags);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Error.Value = ex.Message;
                return 0;
            }
        }
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return 0;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var handle = Bass.CreateStream(source, 0, flags, null, IntPtr.Zero);
            if (handle != 0)
                return handle;
            if (attempt < 2)
                Thread.Sleep(300 * (attempt + 1));
        }
        HttpResponseMessage? response = null;
        try
        {
            response = Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var input = response.Content.ReadAsStream();
            var handle = Create(input, response.Content.Headers.ContentLength ?? 0, flags, response);
            response = null; // The managed BASS source now owns the response, including failures.
            return handle;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            Error.Value = ex.Message;
            return 0;
        }
        finally
        {
            response?.Dispose();
        }
    }

    public static int Create(Stream input, long contentLength, BassFlags flags, IDisposable? owner = null)
    {
        Error.Value = null;
        Stream source = input;
        ManagedStreamSource? managed = null;
        var externalOwner = ReferenceEquals(input, owner) ? null : owner;
        try
        {
            var buffered = !source.CanSeek;
            if (buffered)
                source = new SeekableReadCache(source, contentLength);
            var track = Av3aMp4Track.TryRead(source);
            if (track != null)
            {
                source = new Av3aWaveStream(source, track);
                buffered = false; // PCM cache offers exact file seeking to BASS and BASS_FX.
            }
            source.Position = 0;
            // A zero length tells BASS to obtain the final length from the source when necessary.
            var length = source is Av3aWaveStream ? source.Length : contentLength;
            managed = new ManagedStreamSource(source, length, externalOwner);
            var handle = Bass.CreateStream(buffered ? StreamSystem.Buffer : StreamSystem.NoBuffer,
                flags, managed.CreateProcedures(), IntPtr.Zero);
            if (handle == 0)
                managed.Dispose();
            else
            {
                managed.Closed = () => { ManagedSources.TryRemove(handle, out _); };
                ManagedSources[handle] = managed;
            }
            return handle;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or DllNotFoundException or
                                   BadImageFormatException or EntryPointNotFoundException or OverflowException or
                                   UnauthorizedAccessException)
        {
            Error.Value = ex.Message;
            if (managed != null)
                managed.Dispose();
            else
            {
                source.Dispose();
                externalOwner?.Dispose();
            }
            Console.WriteLine($"[Audio Source Error] {ex.Message}");
            return 0;
        }
    }
}
