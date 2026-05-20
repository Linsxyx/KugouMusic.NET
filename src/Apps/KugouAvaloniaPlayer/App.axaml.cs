using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.GlobalShortcutService;
using KugouAvaloniaPlayer.Services.SystemMediaSession;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using SimpleAudio;
using SukiUI;

namespace KugouAvaloniaPlayer;

public partial class App : Application
{
    private AvaloniaAppServiceProvider? _serviceProvider;
    private ILoggerFactory? _loggerFactory;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        SimpleAudioPlayer.Initialize();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Debug(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        _loggerFactory = new SerilogLoggerFactory(Log.Logger, dispose: true);

        SettingsManager.Load();
        ApplySavedTheme();
        _serviceProvider = new AvaloniaAppServiceProvider
        {
            LoggerFactory = _loggerFactory,
            UiDispatcher = Dispatcher.CurrentDispatcher
        };
        var services = _serviceProvider;

        var vm = services.GetService<MainWindowViewModel>();
        var playerVm = services.GetService<PlayerViewModel>();
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

            InitializeTrayIcon(playerVm, desktop, vm);
            desktop.Exit += (s, e) =>
            {
                globalShortcutService.UnregisterAll();
                systemMediaSessionService.Shutdown();
                ShutdownTrayIcon();
                SimpleAudioPlayer.Free();
                _serviceProvider?.Dispose();
                _loggerFactory?.Dispose();
                Log.CloseAndFlush();
            };
        }

        base.OnFrameworkInitializationCompleted();
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
}
