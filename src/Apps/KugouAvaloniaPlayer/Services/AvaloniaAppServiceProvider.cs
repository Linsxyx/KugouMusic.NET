using System.Net;
using System.Net.Http;
using Avalonia.Threading;
using KuGou.Net.ExternalPlaylists;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.Services.DesktopLyric;
using KugouAvaloniaPlayer.Services.GlobalShortcutService;
using KugouAvaloniaPlayer.Services.Jellyfin;
using KugouAvaloniaPlayer.Services.Startup;
using KugouAvaloniaPlayer.Services.SystemMediaSession;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using Pure.DI;
using SimpleAudio;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using static Pure.DI.Lifetime;
using GlobalShortcutServiceImpl = KugouAvaloniaPlayer.Services.GlobalShortcutService.GlobalShortcutService;
using SystemMediaSessionServiceImpl = KugouAvaloniaPlayer.Services.SystemMediaSession.SystemMediaSessionService;

namespace KugouAvaloniaPlayer.Services;

public sealed partial class AvaloniaAppServiceProvider
{
    public ISessionPersistence SessionPersistence { get; init; } = new KugouSessionPersistence();

    public CookieContainer CookieContainer { get; init; } = new();

    public required ILoggerFactory LoggerFactory { get; init; }

    public required Dispatcher UiDispatcher { get; init; }

    [System.Diagnostics.Conditional("DI")]
    private void Setup() => DI.Setup(nameof(AvaloniaAppServiceProvider))
        .Root<MainWindowViewModel>()
        .Root<PlayerViewModel>()
        .Root<IGlobalShortcutService>()
        .Root<ISystemMediaSessionService>()
        .Root<IStartupActivationServer>()
        .Root<IStartupActivationService>()

        .Bind<ISessionPersistence>().As(Singleton).To(_ => SessionPersistence)
        .Bind<CookieContainer>().As(Singleton).To(_ => CookieContainer)
        .Bind<ILoggerFactory>().As(Singleton).To(_ => LoggerFactory)
        .Bind<Dispatcher>().As(Singleton).To(_ => UiDispatcher)
        .Bind<IUiDispatcherService>().As(Singleton).To<UiDispatcherService>()
        .Bind<ILogger<TT>>().As(Singleton).To((ILoggerFactory loggerFactory) => loggerFactory.CreateLogger<TT>())

        .Bind<KgSessionManager>().As(Singleton).To<KgSessionManager>()
        .Bind<KgSignatureHandler>().To<KgSignatureHandler>()
        .Bind<IKgTransport>().As(Singleton).To((CookieContainer cookieContainer, KgSignatureHandler signatureHandler) =>
            CreateTransport(cookieContainer, signatureHandler))

        .Bind<ISukiToastManager>().As(Singleton).To<SukiToastManager>()
        .Bind<ISukiDialogManager>().As(Singleton).To<SukiDialogManager>()
        .Bind<IHttpClientFactory>().As(Singleton).To<SimpleHttpClientFactory>()
        .Bind<ICreatePlaylistDialogService>().As(Singleton).To<CreatePlaylistDialogService>()
        .Bind<IExternalPlaylistParser>().As(Singleton).To<ExternalPlaylistParser>()
        .Bind<IExternalPlaylistParseStrategy>().As(Singleton).To<NeteasePlaylistParseStrategy>()
        .Bind<IExternalPlaylistParseStrategy>("QQ").As(Singleton).To<QqMusicPlaylistParseStrategy>()
        .Bind<IExternalPlaylistImportService>().As(Singleton).To<ExternalPlaylistImportService>()
        .Bind<ILoginInitializationService>().As(Singleton).To<LoginInitializationService>()
        .Bind<ILoginDialogService>().As(Singleton).To<LoginDialogService>()
        .Bind<INavigationService>().As(Singleton).To<NavigationService>()
        .Bind<IMainWindowService>().As(Singleton).To<MainWindowService>()
        .Bind<IStartupActivationService>().As(Singleton).To<StartupActivationService>()
        .Bind<IStartupActivationServer>().As(Singleton).To<StartupActivationServer>()
        .Bind<IDesktopLyricMousePassthroughService>().As(Singleton).To<DesktopLyricMousePassthroughService>()
        .Bind<IDesktopLyricWindowChromeService>().As(Singleton).To<DesktopLyricWindowChromeService>()
        .Bind<IDesktopLyricWindowService>().As(Singleton).To<DesktopLyricWindowService>()
        .Bind<IGlobalShortcutService>().As(Singleton).To<GlobalShortcutServiceImpl>()
        .Bind<ISystemMediaSessionService>().As(Singleton).To<SystemMediaSessionServiceImpl>()
        .Bind<IFolderPickerService>().As(Singleton).To<FolderPickerService>()
        .Bind<IJellyfinClient>().As(Singleton).To<JellyfinClient>()
        .Bind<ILocalMusicLibraryService>().As(Singleton).To<LocalMusicLibraryService>()
        .Bind<ILocalMusicSearchDialogService>().As(Singleton).To<LocalMusicSearchDialogService>()
        .Bind<IGitHubReleaseService>().As(Singleton).To<GitHubReleaseService>()
        .Bind<IAppUpdateService>().As(Singleton).To<AppUpdateService>()
        .Bind<ISingerViewModelFactory>().To<SingerViewModelFactory>()
        .Bind<IDiscoverTagViewModelFactory>().To<DiscoverTagViewModelFactory>()
        .Bind<IDesktopLyricViewModelFactory>().As(Singleton).To<DesktopLyricViewModelFactory>()
        .Bind<UserCreatedPlaylistCacheService>().As(Singleton).To<UserCreatedPlaylistCacheService>()

        .Bind<PlaybackQueueManager>().As(Singleton).To<PlaybackQueueManager>()
        .Bind<PlaybackQueueCacheService>().As(Singleton).To<PlaybackQueueCacheService>()
        .Bind<LyricsService>().As(Singleton).To<LyricsService>()
        .Bind<FavoritePlaylistService>().As(Singleton).To<FavoritePlaylistService>()
        .Bind<PlaybackHistoryService>().As(Singleton).To<PlaybackHistoryService>()
        .Bind<PersonalFmService>().As(Singleton).To<PersonalFmService>()
        .Bind<PlaybackAudioEffectsService>().As(Singleton).To<PlaybackAudioEffectsService>()
        .Bind<PlaybackVisualizerService>().As(Singleton).To<PlaybackVisualizerService>()
        .Bind<IPlaybackSourceResolver>().As(Singleton).To<PlaybackSourceResolver>()
        .Bind<IPlaybackCoordinator>().As(Singleton).To<PlaybackCoordinator>()
        .Bind<ITransitionAnalysisService>().As(Singleton).To<ManagedBassTransitionAnalysisService>()
        .Bind<PlayerViewModel>().As(Singleton).To<PlayerViewModel>()

        .Bind<LoginViewModel>().To<LoginViewModel>()
        .Bind<SearchViewModel>().As(Singleton).To<SearchViewModel>()
        .Bind<UserCloudViewModel>().To<UserCloudViewModel>()
        .Bind<SettingViewModel>().To<SettingViewModel>()
        .Bind<NowPlayingViewModel>().To<NowPlayingViewModel>()
        .Bind<MainWindowViewModel>().To<MainWindowViewModel>()
        .Bind<DailyRecommendViewModel>().To<DailyRecommendViewModel>()
        .Bind<HistoryViewModel>().To<HistoryViewModel>()
        .Bind<DiscoverViewModel>().To<DiscoverViewModel>()
        .Bind<LocalMusicLibraryViewModel>().To<LocalMusicLibraryViewModel>()
        .Bind<MyPlaylistsViewModel>().To<MyPlaylistsViewModel>()
        .Bind<EqSettingsViewModel>().To<EqSettingsViewModel>()
        .Bind<RankViewModel>().As(Singleton).To<RankViewModel>();

    public TService GetService<TService>()
        where TService : class
    {
        return Resolve<TService>();
    }

    private static KgHttpTransport CreateTransport(CookieContainer cookieContainer, KgSignatureHandler signatureHandler)
    {
        var primaryHandler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookieContainer,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        signatureHandler.InnerHandler = primaryHandler;

        var client = new HttpClient(signatureHandler, disposeHandler: true);
        return new KgHttpTransport(client);
    }
}
