#if KUGOU_LINUX
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaLyrics;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;
using Serilog;

namespace KugouAvaloniaPlayer.Services.DesktopLyric;

// Each session owns one helper and view model. All state capture and actions run
// on the UI thread; a bounded queue keeps a slow native renderer off that thread.
internal sealed class WaylandDesktopLyricSession : IDisposable
{
    private readonly Process _process;
    private readonly DesktopLyricViewModel _viewModel;
    private readonly IUiDispatcherService _dispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<string> _updates = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true
    });
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    private WaylandDesktopLyricSession(Process process, DesktopLyricViewModel viewModel,
        IUiDispatcherService dispatcher)
    {
        _process = process;
        _viewModel = viewModel;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => CaptureState());
        _timer.Stop();
    }

    private static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "kugou-wayland-lyrics");

    public static bool CanTryStart => OperatingSystem.IsLinux()
        && SettingsManager.Settings.EnableNativeDesktopLyrics
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
        && File.Exists(ExecutablePath)
        && Environment.GetEnvironmentVariable("KUGOU_DESKTOP_LYRICS_BACKEND") != "avalonia";

    public event Action? Closed;

    // Ownership of viewModel passes to the session only after a successful handshake.
    public static async Task<WaylandDesktopLyricSession?> TryStartAsync(DesktopLyricViewModel viewModel,
        IUiDispatcherService dispatcher, CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        try
        {
            process.StartInfo.Environment["GDK_BACKEND"] = "wayland";
            if (!process.Start())
                throw new IOException("Could not start Wayland lyrics helper.");
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Log.Information("Wayland lyrics: {Message}", e.Data);
            };
            process.BeginErrorReadLine();
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(TimeSpan.FromSeconds(5));
            var state = CreateState(viewModel);
            await process.StandardInput.WriteLineAsync(
                JsonSerializer.Serialize(state, WaylandLyricsJsonContext.Default.WaylandLyricState).AsMemory(), startup.Token);
            await process.StandardInput.FlushAsync(startup.Token);
            var ready = await process.StandardOutput.ReadLineAsync(startup.Token);
            if (ready == null)
                throw new IOException("Wayland lyrics helper exited before its ready handshake.");
            using var message = JsonDocument.Parse(ready);
            var root = message.RootElement;
            if (root.GetProperty("event").GetString() != "ready" || root.GetProperty("version").GetInt32() != 1)
                throw new IOException("Unsupported Wayland lyrics protocol.");
            cancellationToken.ThrowIfCancellationRequested();
            return new WaylandDesktopLyricSession(process, viewModel, dispatcher);
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                Log.Warning(ex, "原生 Wayland 歌词启动失败，回退到 Avalonia 桌面歌词。");
            StopProcess(process);
            return null;
        }
    }

    public void StartListening()
    {
        _timer.Start();
        CaptureState();
        _ = SendUpdatesAsync();
        _ = ReadEventsAsync();
    }

    public void Unlock()
    {
        _viewModel.Unlock();
        CaptureState();
    }

    private void CaptureState()
    {
        if (_disposed) return;
        var state = CreateState(_viewModel);
        _updates.Writer.TryWrite(JsonSerializer.Serialize(state, WaylandLyricsJsonContext.Default.WaylandLyricState));
    }

    private static WaylandLyricState CreateState(DesktopLyricViewModel vm)
    {
        var player = vm.Player;
        var index = player.CurrentLyricIndex;
        LyricLine? GetLine(int i) => i >= 0 && i < player.RenderLyricLines.Count ? player.RenderLyricLines[i] : null;
        return new WaylandLyricState
        {
            PositionMs = player.CurrentPositionSeconds * 1000,
            Playing = player.IsPlayingAudio,
            Speed = player.PlaybackSpeed,
            Index = index,
            Current = WaylandLyricLine.FromLine(GetLine(index)),
            Next = WaylandLyricLine.FromLine(GetLine(index + 1)),
            Font = vm.LyricFontFamily.Name.Contains('#') ? "sans-serif" : vm.LyricFontFamily.Name,
            FontSize = vm.FontSize,
            Color = (vm.LyricForeground as ISolidColorBrush)?.Color.ToString() ?? "#FFFFFFFF",
            TranslationColor = (vm.TranslationLineForeground as ISolidColorBrush)?.Color.ToString() ?? "#CCFFFFFF",
            Translation = vm.IsTranslationVisible,
            DoubleLine = vm.IsDoubleLineEnabled,
            Vertical = vm.LayoutMode == DesktopLyricLayoutMode.Vertical,
            Alignment = vm.LyricTextAlignment.ToString(),
            Locked = vm.IsLocked,
            ControlsBelow = vm.AreControlsBelow,
            OverFullscreen = SettingsManager.Settings.WaylandDesktopLyricOverFullscreen,
            Placement = vm.LayoutMode == DesktopLyricLayoutMode.Vertical
                ? SettingsManager.Settings.WaylandVerticalDesktopLyricPosition
                : SettingsManager.Settings.WaylandDesktopLyricPosition
        };
    }

    private async Task SendUpdatesAsync()
    {
        try
        {
            await foreach (var message in _updates.Reader.ReadAllAsync(_lifetime.Token))
            {
                await _process.StandardInput.WriteLineAsync(message.AsMemory(), _lifetime.Token);
                await _process.StandardInput.FlushAsync(_lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            if (_lifetime.IsCancellationRequested) return;
            Log.Warning(ex, "Wayland 歌词写入失败，等待读取剩余事件后关闭。");
            // stdout may still contain a lock/placement event emitted just
            // before a crash. Do not dispose its view model before draining it.
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch (Exception cleanupError) when (cleanupError is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Log.Debug(cleanupError, "结束 Wayland 歌词进程。");
            }
        }
    }

    private async Task ReadEventsAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync(_lifetime.Token) is { } message)
            {
                if (message.Length > 65536) throw new IOException("Wayland helper event too large.");
                _dispatcher.RunOrPost(() => HandleEvent(message));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Log.Debug(ex, "读取 Wayland 歌词事件结束。");
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                try
                {
                    await _process.WaitForExitAsync(_lifetime.Token).WaitAsync(TimeSpan.FromSeconds(1));
                    Log.Warning("Wayland 歌词子进程退出，ExitCode={ExitCode}。", _process.ExitCode);
                }
                catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or InvalidOperationException)
                {
                    Log.Debug(ex, "等待 Wayland 歌词子进程退出。");
                }
            }
            _dispatcher.RunOrPost(NotifyClosed);
        }
    }

    private void HandleEvent(string message)
    {
        if (_disposed) return;
        try
        {
            using var json = JsonDocument.Parse(message);
            var root = json.RootElement;
            switch (root.GetProperty("event").GetString())
            {
                case "placement":
                    var placement = root.GetProperty("placement").Deserialize(
                        WaylandLyricsJsonContext.Default.WaylandLyricPlacement);
                    if (placement == null) return;
                    if (root.GetProperty("vertical").GetBoolean())
                        SettingsManager.Settings.WaylandVerticalDesktopLyricPosition = placement;
                    else
                        SettingsManager.Settings.WaylandDesktopLyricPosition = placement;
                    SettingsManager.Save();
                    break;
                case "action":
                    switch (root.GetProperty("name").GetString())
                    {
                        case "lock":
                            _viewModel.IsLocked = true;
                            Log.Information("Wayland 桌面歌词已锁定并保存设置。");
                            break;
                        case "unlock":
                            _viewModel.Unlock();
                            Log.Information("Wayland 桌面歌词已解锁并保存设置。");
                            break;
                        case "larger": _viewModel.IncreaseFontSizeCommand.Execute(null); break;
                        case "smaller": _viewModel.DecreaseFontSizeCommand.Execute(null); break;
                        case "translation": _viewModel.ToggleTranslationVisibilityCommand.Execute(null); break;
                        case "placement": _viewModel.ToggleControlBarPlacementCommand.Execute(null); break;
                        case "play_pause": _viewModel.Player.TogglePlayPauseCommand.Execute(null); break;
                        case "previous": _viewModel.Player.PlayPreviousCommand.Execute(null); break;
                        case "next": _viewModel.Player.PlayNextCommand.Execute(null); break;
                        case "close": NotifyClosed(); return;
                    }
                    break;
            }
            CaptureState();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Log.Warning(ex, "忽略无效的 Wayland 歌词事件。");
        }
    }

    private void NotifyClosed()
    {
        if (_disposed) return;
        Dispose();
        Closed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _updates.Writer.TryComplete();
        _lifetime.Cancel();
        _viewModel.Dispose();
        // Do not block the UI thread waiting for a child that may be unresponsive.
        _ = Task.Run(() => StopProcess(_process));
    }

    private static void StopProcess(Process process)
    {
        try
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(500))
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            Log.Debug(ex, "清理 Wayland 歌词进程。");
        }
        finally { process.Dispose(); }
    }
}
#endif
