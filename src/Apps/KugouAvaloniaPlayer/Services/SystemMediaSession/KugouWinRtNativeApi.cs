#if KUGOU_WINDOWS
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace KugouAvaloniaPlayer.Services.SystemMediaSession;

internal sealed unsafe class KugouWinRtNativeApi : IDisposable
{
    private const uint ExpectedAbiVersion = 1;
    private const string LibraryFileName = "KugouWinRtNative.dll";

    private readonly object _nativeGate = new();
    private readonly Action<uint> _buttonPressed;
    private readonly GCHandle _callbackHandle;
    private readonly delegate* unmanaged[Stdcall]<nint, char*, nuint, char*, nuint, char*, nuint, int> _updateMetadata;
    private readonly delegate* unmanaged[Stdcall]<nint, int, int> _updatePlaybackState;
    private readonly delegate* unmanaged[Stdcall]<nint, long, long, int> _updateTimeline;
    private readonly delegate* unmanaged[Stdcall]<nint, int> _destroy;
    private nint _library;
    private nint _session;
    private bool _libraryMustRemainLoaded;
    private volatile bool _disposed;

    private KugouWinRtNativeApi(nint hwnd, Action<uint> buttonPressed)
    {
        _buttonPressed = buttonPressed;
        _callbackHandle = GCHandle.Alloc(this);

        try
        {
            LibraryPath = Path.Combine(AppContext.BaseDirectory, LibraryFileName);
            _library = NativeLibrary.Load(LibraryPath);

            var getAbiVersion =
                (delegate* unmanaged[Stdcall]<uint>)GetExport("KgWinRt_GetAbiVersion");
            var abiVersion = getAbiVersion();
            if (abiVersion != ExpectedAbiVersion)
            {
                throw new InvalidOperationException(
                    $"{LibraryFileName} ABI 版本不匹配。期望 {ExpectedAbiVersion}，实际 {abiVersion}。");
            }

            var create =
                (delegate* unmanaged[Stdcall]<nint, delegate* unmanaged[Stdcall]<uint, nint, void>, nint, nint*, int>)
                GetExport("KgSmtc_Create");
            _updateMetadata =
                (delegate* unmanaged[Stdcall]<nint, char*, nuint, char*, nuint, char*, nuint, int>)
                GetExport("KgSmtc_UpdateMetadata");
            _updatePlaybackState =
                (delegate* unmanaged[Stdcall]<nint, int, int>)GetExport("KgSmtc_UpdatePlaybackState");
            _updateTimeline =
                (delegate* unmanaged[Stdcall]<nint, long, long, int>)GetExport("KgSmtc_UpdateTimeline");
            _destroy = (delegate* unmanaged[Stdcall]<nint, int>)GetExport("KgSmtc_Destroy");

            nint session = 0;
            var status = create(
                hwnd,
                &HandleButtonPressed,
                GCHandle.ToIntPtr(_callbackHandle),
                &session);
            ThrowIfFailed(status, "创建 Windows 系统媒体会话");
            if (session == 0)
                throw new InvalidOperationException("Rust WinRT DLL 创建成功但返回了空会话句柄。");

            _session = session;
            _libraryMustRemainLoaded = true;
        }
        catch
        {
            ReleaseResources();
            throw;
        }
    }

    public string LibraryPath { get; } = string.Empty;

    public static KugouWinRtNativeApi Load(nint hwnd, Action<uint> buttonPressed)
    {
        if (hwnd == 0)
            throw new ArgumentException("主窗口句柄不可用。", nameof(hwnd));

        return new KugouWinRtNativeApi(hwnd, buttonPressed);
    }

    public int UpdateMetadata(string title, string artist, string? artworkPath)
    {
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            artworkPath ??= string.Empty;

            fixed (char* titlePointer = title)
            fixed (char* artistPointer = artist)
            fixed (char* artworkPointer = artworkPath)
            {
                return _updateMetadata(
                    _session,
                    titlePointer,
                    (nuint)title.Length,
                    artistPointer,
                    (nuint)artist.Length,
                    artworkPointer,
                    (nuint)artworkPath.Length);
            }
        }
    }

    public int UpdatePlaybackState(bool isPlaying)
    {
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _updatePlaybackState(_session, isPlaying ? 1 : 0);
        }
    }

    public int UpdateTimeline(long positionMilliseconds, long durationMilliseconds)
    {
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _updateTimeline(_session, positionMilliseconds, durationMilliseconds);
        }
    }

    public void Dispose()
    {
        lock (_nativeGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            ReleaseResources();
        }
    }

    public static string FormatStatus(int status) => $"0x{unchecked((uint)status):X8}";

    private nint GetExport(string name)
    {
        try
        {
            return NativeLibrary.GetExport(_library, name);
        }
        catch (Exception ex)
        {
            throw new EntryPointNotFoundException(
                $"{LibraryFileName} 缺少导出函数 {name}。", ex);
        }
    }

    private void ReleaseResources()
    {
        if (_session != 0 && _destroy != null)
        {
            try
            {
                _ = _destroy(_session);
            }
            catch
            {
                // Native cleanup must never turn optional WinRT support into an app shutdown failure.
            }

            _session = 0;
        }

        if (_library != 0)
        {
            if (!_libraryMustRemainLoaded)
                NativeLibrary.Free(_library);

            // Once the DLL has registered an event handler, keep its code mapped until process
            // exit. Event revocation prevents future managed callbacks, but Windows does not
            // guarantee that a native delegate already selected for dispatch has stopped running.
            _library = 0;
        }

        if (_callbackHandle.IsAllocated)
            _callbackHandle.Free();
    }

    private static void ThrowIfFailed(int status, string operation)
    {
        if (status < 0)
            throw new InvalidOperationException($"{operation}失败，HRESULT={FormatStatus(status)}。");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void HandleButtonPressed(uint button, nint userData)
    {
        try
        {
            if (userData == 0)
                return;

            var handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is KugouWinRtNativeApi api && !api._disposed)
                api._buttonPressed(button);
        }
        catch
        {
            // Managed exceptions must not cross the native callback boundary.
        }
    }
}
#endif
