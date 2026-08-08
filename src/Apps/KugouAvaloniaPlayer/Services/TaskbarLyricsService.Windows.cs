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
    private readonly object _sync = new();
    private readonly PlayerViewModel _player;
    private Process? _process;
    private StreamWriter? _writer;
    private Timer? _updateTimer;
    private string? _latestMessage;
    private int _messageVersion;
    private int _sentVersion = -1;
    private bool _disposed;

    public TaskbarLyricsService(PlayerViewModel player, ILogger<TaskbarLyricsService> logger)
    {
        _player = player;
        _logger = logger;
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    public bool IsSupported => true;
    public bool IsEnabled { get; private set; }

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_disposed || enabled == IsEnabled)
                return;

            if (enabled)
                StartCore();
            else
                StopCore();
        }
    }

    public void Refresh()
    {
        if (IsEnabled)
            CaptureLatestMessage();
    }

    private void StartCore()
    {
        var executablePath = Path.Combine(AppContext.BaseDirectory, "KugouTaskbarLyrics.exe");
        if (!File.Exists(executablePath))
        {
            _logger.LogWarning("找不到原生任务栏歌词程序: {ExecutablePath}", executablePath);
            return;
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
                return;
            }

            _process = process;
            _writer = process.StandardInput;
            _writer.AutoFlush = true;
            IsEnabled = true;
            CaptureLatestMessage();
            _updateTimer = new Timer(SendLatestMessage, null, TimeSpan.Zero, UpdateInterval);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "启动原生任务栏歌词失败。");
            StopCore();
        }
    }

    private void StopCore()
    {
        IsEnabled = false;
        _updateTimer?.Dispose();
        _updateTimer = null;

        var process = _process;
        _process = null;
        var writer = _writer;
        _writer = null;

        try
        {
            writer?.Dispose();
            if (process is { HasExited: false } && !process.WaitForExit(500))
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "释放任务栏歌词进程时发生异常。");
        }
        finally
        {
            if (process != null)
            {
                process.Exited -= OnProcessExited;
                process.Dispose();
            }
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _process))
                return;

            _updateTimer?.Dispose();
            _updateTimer = null;
            _writer?.Dispose();
            _writer = null;
            _process?.Dispose();
            _process = null;
            IsEnabled = false;
        }
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
            alignment);

        lock (_sync)
        {
            _latestMessage = message;
            _messageVersion++;
        }
    }

    private void SendLatestMessage(object? state)
    {
        lock (_sync)
        {
            if (!IsEnabled || _writer == null || _sentVersion == _messageVersion || _latestMessage == null)
                return;

            try
            {
                _writer.WriteLine(_latestMessage);
                _sentVersion = _messageVersion;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "向原生任务栏歌词发送数据失败。");
                StopCore();
            }
        }
    }

    private static string EncodeText(string value) => Convert.ToHexString(Encoding.UTF8.GetBytes(value));

    private static string FormatNumber(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _player.PropertyChanged -= OnPlayerPropertyChanged;
            StopCore();
        }
        GC.SuppressFinalize(this);
    }
}
#endif
