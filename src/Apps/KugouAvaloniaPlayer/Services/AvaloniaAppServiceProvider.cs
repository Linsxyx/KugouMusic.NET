using System.Net;
using System.Net.Http;
using Avalonia.Threading;
using Jab;
using KuGou.Net.Clients;
using KuGou.Net.ExternalPlaylists;
using KuGou.Net.Infrastructure;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using KugouAvaloniaPlayer.Services.DesktopLyric;
using KugouAvaloniaPlayer.Services.GlobalShortcutService;
using KugouAvaloniaPlayer.Services.SystemMediaSession;
using KugouAvaloniaPlayer.ViewModels;
using Microsoft.Extensions.Logging;
using SimpleAudio;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using GlobalShortcutServiceImpl = KugouAvaloniaPlayer.Services.GlobalShortcutService.GlobalShortcutService;
using SystemMediaSessionServiceImpl = KugouAvaloniaPlayer.Services.SystemMediaSession.SystemMediaSessionService;

namespace KugouAvaloniaPlayer.Services;

[ServiceProviderModule]
[Singleton<ISukiToastManager, SukiToastManager>]
[Singleton<ISukiDialogManager, SukiDialogManager>]
[Singleton<IHttpClientFactory, SimpleHttpClientFactory>]
[Singleton<ICreatePlaylistDialogService, CreatePlaylistDialogService>]
[Singleton<IExternalPlaylistParser, ExternalPlaylistParser>]
[Singleton<IExternalPlaylistParseStrategy, NeteasePlaylistParseStrategy>]
[Singleton<IExternalPlaylistParseStrategy, QqMusicPlaylistParseStrategy>]
[Singleton<IExternalPlaylistImportService, ExternalPlaylistImportService>]
[Singleton<ILoginDialogService, LoginDialogService>]
[Singleton<INavigationService, NavigationService>]
[Singleton<IMainWindowService, MainWindowService>]
[Singleton<IDesktopLyricMousePassthroughService, DesktopLyricMousePassthroughService>]
[Singleton<IDesktopLyricWindowService, DesktopLyricWindowService>]
[Singleton<IGlobalShortcutService, GlobalShortcutServiceImpl>]
[Singleton<ISystemMediaSessionService, SystemMediaSessionServiceImpl>]
[Singleton<IFolderPickerService, FolderPickerService>]
[Singleton<IGitHubReleaseService, GitHubReleaseService>]
[Singleton<IAppUpdateService, AppUpdateService>]
[Transient<LoginViewModel>]
[Transient<SearchViewModel>]
[Transient<UserViewModel>]
[Transient<NowPlayingViewModel>]
[Transient<MainWindowViewModel>]
[Transient<DailyRecommendViewModel>]
[Transient<HistoryViewModel>]
[Transient<DiscoverViewModel>]
[Transient<MyPlaylistsViewModel>]
[Transient<EqSettingsViewModel>]
[Transient<RankViewModel>]
[Transient<ISingerViewModelFactory, SingerViewModelFactory>]
[Singleton<IDesktopLyricViewModelFactory, DesktopLyricViewModelFactory>]
[Singleton<PlaybackQueueManager>]
[Singleton<LyricsService>]
[Singleton<FavoritePlaylistService>]
[Singleton<PlaybackHistoryService>]
[Singleton<PersonalFmService>]
[Singleton<PlaybackAudioEffectsService>]
[Singleton<PlaybackVisualizerService>]
[Singleton<IPlaybackSourceResolver, PlaybackSourceResolver>]
[Singleton<IPlaybackCoordinator, PlaybackCoordinator>]
[Singleton<ITransitionAnalysisService, ManagedBassTransitionAnalysisService>]
[Singleton<PlayerViewModel>]
public interface IAvaloniaAppServiceModule;

[ServiceProvider(RootServices = new[]
{
    typeof(MainWindowViewModel),
    typeof(PlayerViewModel),
    typeof(IGlobalShortcutService),
    typeof(ISystemMediaSessionService)
})]
[Import<IKuGouServiceModule>]
[Import<IAvaloniaAppServiceModule>]
[Singleton<ISessionPersistence>(Instance = nameof(SessionPersistence))]
[Singleton<CookieContainer>(Instance = nameof(CookieContainer))]
[Singleton<ILoggerFactory>(Instance = nameof(LoggerFactory))]
[Singleton<IUiDispatcherService>(Factory = nameof(CreateUiDispatcherService))]
[Singleton<KgSessionManager>]
[Transient<KgSignatureHandler>]
[Singleton<IKgTransport>(Factory = nameof(CreateTransport))]
[Singleton<ILogger<RawLoginApi>>(Factory = nameof(CreateRawLoginApiLogger))]
[Singleton<ILogger<RawPlaylistApi>>(Factory = nameof(CreateRawPlaylistApiLogger))]
[Singleton<ILogger<RawDeviceApi>>(Factory = nameof(CreateRawDeviceApiLogger))]
[Singleton<ILogger<LoginClient>>(Factory = nameof(CreateLoginClientLogger))]
[Singleton<ILogger<RegisterClient>>(Factory = nameof(CreateRegisterClientLogger))]
[Singleton<ILogger<SingerViewModel>>(Factory = nameof(CreateSingerViewModelLogger))]
[Singleton<ILogger<AppUpdateService>>(Factory = nameof(CreateAppUpdateServiceLogger))]
[Singleton<ILogger<SearchViewModel>>(Factory = nameof(CreateSearchViewModelLogger))]
[Singleton<ILogger<RankViewModel>>(Factory = nameof(CreateRankViewModelLogger))]
[Singleton<ILogger<LoginViewModel>>(Factory = nameof(CreateLoginViewModelLogger))]
[Singleton<ILogger<PlayerViewModel>>(Factory = nameof(CreatePlayerViewModelLogger))]
[Singleton<ILogger<NowPlayingViewModel>>(Factory = nameof(CreateNowPlayingViewModelLogger))]
[Singleton<ILogger<DiscoverViewModel>>(Factory = nameof(CreateDiscoverViewModelLogger))]
[Singleton<ILogger<MyPlaylistsViewModel>>(Factory = nameof(CreateMyPlaylistsViewModelLogger))]
[Singleton<ILogger<MainWindowViewModel>>(Factory = nameof(CreateMainWindowViewModelLogger))]
[Singleton<ILogger<DailyRecommendViewModel>>(Factory = nameof(CreateDailyRecommendViewModelLogger))]
[Singleton<ILogger<FavoritePlaylistService>>(Factory = nameof(CreateFavoritePlaylistServiceLogger))]
[Singleton<ILogger<ExternalPlaylistImportService>>(Factory = nameof(CreateExternalPlaylistImportServiceLogger))]
[Singleton<ILogger<NeteasePlaylistParseStrategy>>(Factory = nameof(CreateNeteasePlaylistParseStrategyLogger))]
[Singleton<ILogger<QqMusicPlaylistParseStrategy>>(Factory = nameof(CreateQqMusicPlaylistParseStrategyLogger))]
[Singleton<ILogger<GitHubReleaseService>>(Factory = nameof(CreateGitHubReleaseServiceLogger))]
[Singleton<ILogger<GlobalShortcutServiceImpl>>(Factory = nameof(CreateGlobalShortcutServiceLogger))]
[Singleton<ILogger<LyricsService>>(Factory = nameof(CreateLyricsServiceLogger))]
[Singleton<ILogger<PlaybackCoordinator>>(Factory = nameof(CreatePlaybackCoordinatorLogger))]
[Singleton<ILogger<PlaybackAudioEffectsService>>(Factory = nameof(CreatePlaybackAudioEffectsServiceLogger))]
[Singleton<ILogger<PersonalFmService>>(Factory = nameof(CreatePersonalFmServiceLogger))]
[Singleton<ILogger<PlaybackHistoryService>>(Factory = nameof(CreatePlaybackHistoryServiceLogger))]
[Singleton<ILogger<SystemMediaSessionServiceImpl>>(Factory = nameof(CreateSystemMediaSessionServiceLogger))]
public sealed partial class AvaloniaAppServiceProvider
{
    public ISessionPersistence SessionPersistence { get; init; } = new KugouSessionPersistence();

    public CookieContainer CookieContainer { get; init; } = new();

    public required ILoggerFactory LoggerFactory { get; init; }

    public required Dispatcher UiDispatcher { get; init; }

    public IUiDispatcherService CreateUiDispatcherService()
    {
        return new UiDispatcherService(UiDispatcher);
    }

    public IKgTransport CreateTransport(CookieContainer cookieContainer, KgSignatureHandler signatureHandler)
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

    public ILogger<RawLoginApi> CreateRawLoginApiLogger() => LoggerFactory.CreateLogger<RawLoginApi>();
    public ILogger<RawPlaylistApi> CreateRawPlaylistApiLogger() => LoggerFactory.CreateLogger<RawPlaylistApi>();
    public ILogger<RawDeviceApi> CreateRawDeviceApiLogger() => LoggerFactory.CreateLogger<RawDeviceApi>();
    public ILogger<LoginClient> CreateLoginClientLogger() => LoggerFactory.CreateLogger<LoginClient>();
    public ILogger<RegisterClient> CreateRegisterClientLogger() => LoggerFactory.CreateLogger<RegisterClient>();
    public ILogger<SingerViewModel> CreateSingerViewModelLogger() => LoggerFactory.CreateLogger<SingerViewModel>();
    public ILogger<AppUpdateService> CreateAppUpdateServiceLogger() => LoggerFactory.CreateLogger<AppUpdateService>();
    public ILogger<SearchViewModel> CreateSearchViewModelLogger() => LoggerFactory.CreateLogger<SearchViewModel>();
    public ILogger<RankViewModel> CreateRankViewModelLogger() => LoggerFactory.CreateLogger<RankViewModel>();
    public ILogger<LoginViewModel> CreateLoginViewModelLogger() => LoggerFactory.CreateLogger<LoginViewModel>();
    public ILogger<PlayerViewModel> CreatePlayerViewModelLogger() => LoggerFactory.CreateLogger<PlayerViewModel>();
    public ILogger<NowPlayingViewModel> CreateNowPlayingViewModelLogger() => LoggerFactory.CreateLogger<NowPlayingViewModel>();
    public ILogger<DiscoverViewModel> CreateDiscoverViewModelLogger() => LoggerFactory.CreateLogger<DiscoverViewModel>();
    public ILogger<MyPlaylistsViewModel> CreateMyPlaylistsViewModelLogger() => LoggerFactory.CreateLogger<MyPlaylistsViewModel>();
    public ILogger<MainWindowViewModel> CreateMainWindowViewModelLogger() => LoggerFactory.CreateLogger<MainWindowViewModel>();
    public ILogger<DailyRecommendViewModel> CreateDailyRecommendViewModelLogger() => LoggerFactory.CreateLogger<DailyRecommendViewModel>();
    public ILogger<FavoritePlaylistService> CreateFavoritePlaylistServiceLogger() => LoggerFactory.CreateLogger<FavoritePlaylistService>();
    public ILogger<ExternalPlaylistImportService> CreateExternalPlaylistImportServiceLogger() => LoggerFactory.CreateLogger<ExternalPlaylistImportService>();
    public ILogger<NeteasePlaylistParseStrategy> CreateNeteasePlaylistParseStrategyLogger() => LoggerFactory.CreateLogger<NeteasePlaylistParseStrategy>();
    public ILogger<QqMusicPlaylistParseStrategy> CreateQqMusicPlaylistParseStrategyLogger() => LoggerFactory.CreateLogger<QqMusicPlaylistParseStrategy>();
    public ILogger<GitHubReleaseService> CreateGitHubReleaseServiceLogger() => LoggerFactory.CreateLogger<GitHubReleaseService>();
    public ILogger<GlobalShortcutServiceImpl> CreateGlobalShortcutServiceLogger() => LoggerFactory.CreateLogger<GlobalShortcutServiceImpl>();
    public ILogger<LyricsService> CreateLyricsServiceLogger() => LoggerFactory.CreateLogger<LyricsService>();
    public ILogger<PlaybackCoordinator> CreatePlaybackCoordinatorLogger() => LoggerFactory.CreateLogger<PlaybackCoordinator>();
    public ILogger<PlaybackAudioEffectsService> CreatePlaybackAudioEffectsServiceLogger() => LoggerFactory.CreateLogger<PlaybackAudioEffectsService>();
    public ILogger<PersonalFmService> CreatePersonalFmServiceLogger() => LoggerFactory.CreateLogger<PersonalFmService>();
    public ILogger<PlaybackHistoryService> CreatePlaybackHistoryServiceLogger() => LoggerFactory.CreateLogger<PlaybackHistoryService>();
    public ILogger<SystemMediaSessionServiceImpl> CreateSystemMediaSessionServiceLogger() => LoggerFactory.CreateLogger<SystemMediaSessionServiceImpl>();
}
