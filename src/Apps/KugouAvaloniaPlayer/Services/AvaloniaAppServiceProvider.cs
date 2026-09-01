using System.Net;
using System.Net.Http;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
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
    [System.Diagnostics.Conditional("DI")]
    private static void Setup() => DI.Setup()
        .Hint(Hint.Resolve, "Off")
        .Root<Owned<DesktopAppRoot>>(
            nameof(CreateRoot),
            kind: RootKinds.Internal | RootKinds.Partial | RootKinds.Method)
        .Bind<DesktopAppRoot>().To<DesktopAppRoot>()

        .RootArg<ILoggerFactory>("loggerFactory")
        .RootArg<Dispatcher>("uiDispatcher")
        .Bind<ISessionPersistence>().As(Singleton).To<KugouSessionPersistence>()
        .Bind<CookieContainer>().As(Singleton).To<CookieContainer>()
        .Bind<IUiDispatcherService>().As(Singleton).To<UiDispatcherService>()
        .Bind<IUiPreferencesState>().As(Singleton).To<UiPreferencesState>()
        .Bind<IMessenger>().As(Singleton).To(_ => new WeakReferenceMessenger())
        .Bind<ILogger<TT>>().As(Singleton).To((ILoggerFactory loggerFactory) => loggerFactory.CreateLogger<TT>())

        .Bind<KgSessionManager>().As(Singleton).To<KgSessionManager>()
        .Bind<KgSignatureHandler>().To<KgSignatureHandler>()
        .Bind<HttpClient>().As(Singleton).To((CookieContainer cookieContainer, KgSignatureHandler signatureHandler) =>
            CreateHttpClient(cookieContainer, signatureHandler))
        .Bind<IKgTransport>().As(Singleton).To<KgHttpTransport>()

        .Bind<ISukiToastManager>().As(Singleton).To<SukiToastManager>()
        .Bind<ISukiDialogManager>().As(Singleton).To<SukiDialogManager>()
        .Bind<IHttpClientFactory>().As(Singleton).To<SimpleHttpClientFactory>()
        .Bind<ICreatePlaylistDialogService>().As(Singleton).To<CreatePlaylistDialogService>()
        .Bind<IExternalPlaylistParser>().As(Singleton).To<ExternalPlaylistParser>()
        .Bind<IExternalPlaylistParseStrategy>().As(Singleton).To<NeteasePlaylistParseStrategy>()
        .Bind<IExternalPlaylistParseStrategy>("QQ").As(Singleton).To<QqMusicPlaylistParseStrategy>()
        .Bind<IExternalPlaylistImportService>().As(Singleton).To<ExternalPlaylistImportService>()
        .Bind<ILoginInitializationService>().As(Singleton).To<LoginInitializationService>()
        .Bind<IVipEntitlementService>().As(Singleton).To<VipEntitlementService>()
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
        .Bind<ITaskbarLyricsService>().As(Singleton).To<TaskbarLyricsService>()
        .Bind<IFolderPickerService>().As(Singleton).To<FolderPickerService>()
        .Bind<ISongInteractionService>().As(Singleton).To<SongInteractionService>()
        .Bind<IJellyfinClient>().As(Singleton).To<JellyfinClient>()
        .Bind<ILocalMusicLibraryService>().As(Singleton).To<LocalMusicLibraryService>()
        .Bind<ILocalMusicSearchDialogService>().As(Singleton).To<LocalMusicSearchDialogService>()
        .Bind<ITrackPlaylistSearchDialogService>().As(Singleton).To<TrackPlaylistSearchDialogService>()
        .Bind<ILocalSingerMatchService>().As(Singleton).To<LocalSingerMatchService>()
        .Bind<ILocalLyricMatchService>().As(Singleton).To<LocalLyricMatchService>()
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
        .Bind<IPlaybackSourceRecoveryService>().As(Singleton).To<PlaybackSourceRecoveryService>()
        .Bind<IPlaybackCoordinator>().As(Singleton).To<PlaybackCoordinator>()
        .Bind<ITransitionAnalysisService>().As(Singleton).To<ManagedBassTransitionAnalysisService>()
        .Bind<PlayerViewModel>().As(Singleton).To<PlayerViewModel>()
        .Bind<IPlaybackCommands>().To((PlayerViewModel playerViewModel) => playerViewModel)

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
        .Bind<AdvancedAudioEffectsViewModel>().To<AdvancedAudioEffectsViewModel>()
        .Bind<RankViewModel>().As(Singleton).To<RankViewModel>();

    internal partial Owned<DesktopAppRoot> CreateRoot(
        ILoggerFactory loggerFactory,
        Dispatcher uiDispatcher);

    private static HttpClient CreateHttpClient(CookieContainer cookieContainer, KgSignatureHandler signatureHandler)
    {
        var primaryHandler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookieContainer,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        signatureHandler.InnerHandler = primaryHandler;

        return new HttpClient(signatureHandler, disposeHandler: true);
    }
}

internal readonly record struct DesktopAppRoot(
    MainWindowViewModel MainWindowViewModel,
    PlayerViewModel PlayerViewModel,
    IMainWindowService MainWindowService,
    IGlobalShortcutService GlobalShortcutService,
    ISystemMediaSessionService SystemMediaSessionService,
    ITaskbarLyricsService TaskbarLyricsService,
    IStartupActivationServer StartupActivationServer);
