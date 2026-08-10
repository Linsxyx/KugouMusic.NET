#if KUGOU_WINDOWS
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;

namespace KugouAvaloniaPlayer.Services;

public sealed class TaskbarLyricsService : ITaskbarLyricsService
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(16);
    private readonly ILogger<TaskbarLyricsService> _logger;
    private readonly object _lifecycleGate = new();
    private readonly object _sync = new();
    private readonly PlayerViewModel _player;
    private Process? _process;
    private StreamWriter? _writer;
    private Timer? _updateTimer;
    private string? _latestMessage;
    private int _messageVersion;
    private int _sentVersion = -1;
    private int _sendActive;
    private bool _isEnabled;
    private bool _disposed;

    public TaskbarLyricsService(PlayerViewModel player, ILogger<TaskbarLyricsService> logger)
    {
        _player = player;
        _logger = logger;
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    public bool IsSupported => true;
    public bool IsEnabled
    {
        get
        {
            lock (_sync)
                return _isEnabled;
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_lifecycleGate)
        {
            DetachedSession? sessionToDispose;
            lock (_sync)
            {
                if (_disposed || enabled == _isEnabled)
                    return;

                sessionToDispose = enabled
                    ? StartCoreLocked()
                    : DetachSessionLocked();
            }

            DisposeSession(sessionToDispose);
        }
    }

    public void Refresh()
    {
        if (IsEnabled)
            CaptureLatestMessage();
    }

    private DetachedSession? StartCoreLocked()
    {
        var executablePath = Path.Combine(AppContext.BaseDirectory, "KugouTaskbarLyrics.exe");
        if (!File.Exists(executablePath))
        {
            _logger.LogWarning("找不到原生任务栏歌词程序: {ExecutablePath}", executablePath);
            return null;
        }

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                },
                EnableRaisingEvents = true
            };
            process.Exited += OnProcessExited;
            if (!process.Start())
            {
                process.Dispose();
                return null;
            }

            _process = process;
            _writer = process.StandardInput;
            _writer.AutoFlush = true;
            _isEnabled = true;
            _sentVersion = -1;
            CaptureLatestMessage();
            _updateTimer = new Timer(SendLatestMessage, null, TimeSpan.Zero, UpdateInterval);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "启动原生任务栏歌词失败。");
            return DetachSessionLocked();
        }
    }

    private DetachedSession? DetachSessionLocked()
    {
        _isEnabled = false;

        var process = _process;
        var writer = _writer;
        var timer = _updateTimer;

        _process = null;
        _writer = null;
        _updateTimer = null;

        return process == null && writer == null && timer == null
            ? null
            : new DetachedSession(process, writer, timer);
    }

    private void DisposeSession(DetachedSession? session)
    {
        if (session == null)
            return;

        session.Timer?.Dispose();
        var process = session.Process;

        try
        {
            if (process != null)
                process.Exited -= OnProcessExited;

            session.Writer?.Dispose();
            if (process is { HasExited: false } && !process.WaitForExit(500))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(500);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "释放任务栏歌词进程时发生异常。");
        }
        finally
        {
            if (process != null)
                process.Dispose();
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        DetachedSession? session;
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _process))
                return;

            session = DetachSessionLocked();
        }

        DisposeSession(session);
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsEnabled)
            return;

        if (e.PropertyName is nameof(PlayerViewModel.CurrentPositionSeconds)
            or nameof(PlayerViewModel.CurrentLyricLine)
            or nameof(PlayerViewModel.IsPlayingAudio)
            or nameof(PlayerViewModel.PlaybackSpeed))
        {
            CaptureLatestMessage();
        }
    }

    private void CaptureLatestMessage()
    {
        var line = _player.CurrentLyricLine;
        var primary = line?.Content ?? string.Empty;
        var settings = SettingsManager.Settings;
        var secondary = settings.TaskbarLyricsShowTranslation
            ? line?.Translation ?? string.Empty
            : string.Empty;
        var alignment = settings.TaskbarLyricsAlignment == LyricAlignmentOption.Right ? "1" : "0";
        var fontFamily = string.IsNullOrWhiteSpace(settings.TaskbarLyricsFontFamily)
            ? "Microsoft YaHei UI"
            : settings.TaskbarLyricsFontFamily.Trim();
        var fontSize = Math.Clamp(settings.TaskbarLyricsFontSize, 12, 24);
        var words = line?.Words
            .Select(word => string.Join(',',
                FormatNumber(word.StartTime),
                FormatNumber(word.Duration),
                EncodeText(word.Text)))
            .ToArray() ?? [];

        var message = string.Join('\t',
            "U",
            FormatNumber(_player.CurrentPositionSeconds * 1000),
            _player.IsPlayingAudio ? "1" : "0",
            FormatNumber(_player.PlaybackSpeed),
            FormatNumber(line?.StartTime ?? 0),
            FormatNumber(line?.Duration ?? 0),
            EncodeText(primary),
            EncodeText(secondary),
            string.Join(';', words),
            alignment,
            EncodeText(fontFamily),
            fontSize.ToString(CultureInfo.InvariantCulture),
            FormatArgb(settings.TaskbarLyricsUnplayedColor, 0xFF2E2E2E),
            FormatArgb(settings.TaskbarLyricsPlayedColor, 0xFF268EEB));

        lock (_sync)
        {
            _latestMessage = message;
            _messageVersion++;
        }
    }

    private void SendLatestMessage(object? state)
    {
        if (Interlocked.CompareExchange(ref _sendActive, 1, 0) != 0)
            return;

        try
        {
            StreamWriter? writer;
            string? message;
            int version;

            lock (_sync)
            {
                if (!_isEnabled || _writer == null || _sentVersion == _messageVersion || _latestMessage == null)
                    return;

                writer = _writer;
                message = _latestMessage;
                version = _messageVersion;
            }

            try
            {
                writer.WriteLine(message);

                lock (_sync)
                {
                    if (ReferenceEquals(writer, _writer))
                        _sentVersion = version;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "向原生任务栏歌词发送数据失败。");
                lock (_lifecycleGate)
                {
                    DetachedSession? failedSession = null;
                    lock (_sync)
                    {
                        if (ReferenceEquals(writer, _writer))
                            failedSession = DetachSessionLocked();
                    }

                    DisposeSession(failedSession);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _sendActive, 0);
        }
    }

    private static string EncodeText(string value) => Convert.ToHexString(Encoding.UTF8.GetBytes(value));

    private static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatArgb(string? value, uint fallback)
    {
        var hex = value?.Trim().TrimStart('#');
        if (hex?.Length == 6)
            hex = $"FF{hex}";

        return hex?.Length == 8 && uint.TryParse(
            hex,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var argb)
                ? $"0x{argb:X8}"
                : $"0x{fallback:X8}";
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            DetachedSession? session;
            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                session = DetachSessionLocked();
            }

            _player.PropertyChanged -= OnPlayerPropertyChanged;
            DisposeSession(session);
        }
        GC.SuppressFinalize(this);
    }

    private sealed record DetachedSession(Process? Process, StreamWriter? Writer, Timer? Timer);
}
#endif
