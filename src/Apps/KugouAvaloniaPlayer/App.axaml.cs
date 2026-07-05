using System;
using System.IO;
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
            _serviceProvider = new AvaloniaAppServiceProvider
            {
                LoggerFactory = _loggerFactory,
                UiDispatcher = Dispatcher.CurrentDispatcher
            };
            var services = _serviceProvider;

            var vm = services.GetService<MainWindowViewModel>();
            var playerVm = services.GetService<PlayerViewModel>();
            var startupActivationServer = services.GetService<IStartupActivationServer>();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow
                {
                    DataContext = vm
                };
                desktop.MainWindow = mainWindow;

                var globalShortcutService = services.GetService<IGlobalShortcutService>();
                var systemMediaSessionService = services.GetService<ISystemMediaSessionService>();

                void InitializeGlobalShortcuts(object? _, EventArgs __)
                {
                    mainWindow.Opened -= InitializeGlobalShortcuts;
                    globalShortcutService.Initialize(mainWindow);
                    globalShortcutService.LoadFromSettings(SettingsManager.Settings.GlobalShortcuts);
                    systemMediaSessionService.Initialize(mainWindow, playerVm);
                }

                mainWindow.Opened += InitializeGlobalShortcuts;
                startupActivationServer.Start();

                InitializeTrayIcon(playerVm, desktop, vm);
                desktop.Exit += (s, e) =>
                {
                    startupActivationServer.Stop();
                    globalShortcutService.UnregisterAll();
                    systemMediaSessionService.Shutdown();
                    ShutdownTrayIcon();
                    _imageLoader?.Dispose();
                    SimpleAudioPlayer.Free();
                    Program.ShutdownStartupCoordinator();
                    _serviceProvider?.Dispose();
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
            maxMemoryEntries: 200,
            maxMemoryBytes: 32L * 1024 * 1024,
            maxDiskBytes: 256L * 1024 * 1024);

        ImageLoader.AsyncImageLoader = _imageLoader;

        if (!ReferenceEquals(previousLoader, _imageLoader))
            previousLoader.Dispose();
    }

}
