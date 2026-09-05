using System;
using System.IO;
using System.Net.Http;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.GlobalShortcutService;
using KugouAvaloniaPlayer.Services.Startup;
using KugouAvaloniaPlayer.Services.SystemMediaSession;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views;
using Serilog;
using Serilog.Extensions.Logging;
using SimpleAudio;
using SukiUI;

namespace KugouAvaloniaPlayer;

public partial class App : Application
{
    private SerilogLoggerFactory? _loggerFactory;
    private AvaloniaAppServiceProvider? _serviceProvider;
    private BoundedDiskCachedWebImageLoader? _imageLoader;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        CrashReporting.ConfigureLogging();
        CrashReporting.RegisterUiThreadHandler();
        try
        {
            ConfigureImageLoader();
            SettingsManager.Load();
            SimpleAudioPlayer.Initialize(SettingsManager.Settings.AudioOutputDeviceId);

            _loggerFactory = new SerilogLoggerFactory(Log.Logger, true);

            ApplySavedTheme();
            AppFontService.ApplyGlobalFont(this);
            _serviceProvider = new AvaloniaAppServiceProvider();
            var ownedRoot = _serviceProvider.CreateRoot(
                _loggerFactory,
                Dispatcher.CurrentDispatcher);
            var (vm,
                playerVm,
                mainWindowService,
                globalShortcutService,
                systemMediaSessionService,
                taskbarLyricsService,
                startupActivationServer) = ownedRoot.Value;

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow
                {
                    DataContext = vm
                };
                desktop.MainWindow = mainWindow;

                void InitializeGlobalShortcuts(object? _, EventArgs __)
                {
                    mainWindow.Opened -= InitializeGlobalShortcuts;
                    globalShortcutService.Initialize(mainWindow);
                    globalShortcutService.LoadFromSettings(SettingsManager.Settings.GlobalShortcuts);
                    systemMediaSessionService.Initialize(mainWindow, playerVm);
                }

                mainWindow.Opened += InitializeGlobalShortcuts;
                startupActivationServer.Start();
                if (taskbarLyricsService.IsSupported)
                    taskbarLyricsService.SetEnabled(SettingsManager.Settings.EnableTaskbarLyrics);

#if KUGOU_MACOS
                var activatableLifetime = this.TryGetFeature<IActivatableLifetime>();

                void OnApplicationActivated(object? _, ActivatedEventArgs e)
                {
                    if (e.Kind == ActivationKind.Reopen)
                        mainWindowService.ShowMainWindow();
                }

                if (activatableLifetime != null)
                    activatableLifetime.Activated += OnApplicationActivated;

                desktop.ShutdownRequested += (_, _) => mainWindow.CanClose = true;
#endif

                InitializeTrayIcon(playerVm, desktop, vm, mainWindowService);
                desktop.Exit += (s, e) =>
                {
#if KUGOU_MACOS
                    if (activatableLifetime != null)
                        activatableLifetime.Activated -= OnApplicationActivated;
#endif
                    globalShortcutService.UnregisterAll();
                    ShutdownTrayIcon();
                    _imageLoader?.Dispose();
                    ownedRoot.Dispose();
                    _serviceProvider?.Dispose();
                    SimpleAudioPlayer.Free();
                    Program.ShutdownStartupCoordinator();
                    _loggerFactory?.Dispose();
                    Log.CloseAndFlush();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "应用启动失败");
            Log.CloseAndFlush();
            throw;
        }
    }

    private static void ApplySavedTheme()
    {
        var theme = SettingsManager.Settings.AppTheme switch
        {
            AppSettings.ThemeDark => ThemeVariant.Dark,
            AppSettings.ThemeLight => ThemeVariant.Light,
            _ => null
        };

        if (theme != null)
            SukiTheme.GetInstance().ChangeBaseTheme(theme);
    }

    private void ConfigureImageLoader()
    {
        var cacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "kugou",
            "image-cache");

        var previousLoader = ImageLoader.AsyncImageLoader;
        _imageLoader = new BoundedDiskCachedWebImageLoader(
            cacheFolder,
            TimeSpan.FromDays(7),
            httpClient: new HttpClient(KuGou.Net.Infrastructure.Http.KgPrimaryHandler.Create()),
            maxMemoryEntries: 200,
            maxMemoryBytes: 32L * 1024 * 1024,
            maxDiskBytes: 256L * 1024 * 1024);

        ImageLoader.AsyncImageLoader = _imageLoader;

        if (!ReferenceEquals(previousLoader, _imageLoader))
            previousLoader.Dispose();
    }

}
