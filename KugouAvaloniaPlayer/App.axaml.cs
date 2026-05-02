using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KuGou.Net.Infrastructure;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.Services.DesktopLyric;
using KugouAvaloniaPlayer.Services.GlobalShortcutService;
using KugouAvaloniaPlayer.Services.SystemMediaSession;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimpleAudio;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        SimpleAudioPlayer.Initialize();
        var collection = new ServiceCollection();
        collection.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
            builder.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
            builder.AddDebug();
            builder.AddConsole();
        });
        collection.AddSingleton<ISessionPersistence, KugouSessionPersistence>();
        collection.AddKuGouSdk();

        collection.AddSingleton<ISukiToastManager, SukiToastManager>();
        collection.AddSingleton<ISukiDialogManager, SukiDialogManager>();
        collection.AddSingleton<ICreatePlaylistDialogService, CreatePlaylistDialogService>();
        collection.AddSingleton<IExternalPlaylistParseStrategy, NeteasePlaylistParseStrategy>();
        collection.AddSingleton<IExternalPlaylistParseStrategy, QqMusicPlaylistParseStrategy>();
        collection.AddSingleton<IExternalPlaylistImportService, ExternalPlaylistImportService>();
        collection.AddSingleton<ILoginDialogService, LoginDialogService>();
        collection.AddSingleton<INavigationService, NavigationService>();
        collection.AddSingleton<IMainWindowService, MainWindowService>();
        collection.AddSingleton<IDesktopLyricMousePassthroughService, DesktopLyricMousePassthroughService>();
        collection.AddSingleton<IDesktopLyricWindowService, DesktopLyricWindowService>();
        collection.AddSingleton<IGlobalShortcutService, GlobalShortcutService>();
        collection.AddSingleton<ISystemMediaSessionService, SystemMediaSessionService>();
        collection.AddSingleton<IFolderPickerService, FolderPickerService>();
        collection.AddSingleton<IGitHubReleaseService, GitHubReleaseService>();

        SettingsManager.Load();

        // 注册 ViewModels
        collection.AddTransient<LoginViewModel>();
        collection.AddTransient<SearchViewModel>();
        collection.AddTransient<SingerViewModel>();
        collection.AddTransient<UserViewModel>();
        collection.AddTransient<MainWindowViewModel>();
        collection.AddTransient<DailyRecommendViewModel>();
        collection.AddTransient<DiscoverViewModel>();
        collection.AddTransient<MyPlaylistsViewModel>();
        collection.AddTransient<EqSettingsViewModel>();
        collection.AddTransient<ISingerViewModelFactory, SingerViewModelFactory>();
        collection.AddSingleton<IDesktopLyricViewModelFactory, DesktopLyricViewModelFactory>();

        collection.AddSingleton<PlaybackQueueManager>();
        collection.AddSingleton<LyricsService>();
        collection.AddSingleton<FavoritePlaylistService>();
        collection.AddSingleton<IPlaybackSourceResolver, PlaybackSourceResolver>();
        collection.AddSingleton<IPlaybackCoordinator, PlaybackCoordinator>();
        collection.AddSingleton<ITransitionAnalysisService, ManagedBassTransitionAnalysisService>();
        collection.AddSingleton<PlayerViewModel>();

        collection.AddTransient<RankViewModel>();

        _serviceProvider = collection.BuildServiceProvider();
        var services = _serviceProvider;

        var vm = services.GetRequiredService<MainWindowViewModel>();
        var playerVm = services.GetRequiredService<PlayerViewModel>();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = vm
            };
            desktop.MainWindow = mainWindow;

            var globalShortcutService = services.GetRequiredService<IGlobalShortcutService>();
            var systemMediaSessionService = services.GetRequiredService<ISystemMediaSessionService>();

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
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
