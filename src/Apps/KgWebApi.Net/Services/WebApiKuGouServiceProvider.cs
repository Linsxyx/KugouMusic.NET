using System.Net;
using KgWebApi.Net.Controllers;
using KgWebApi.Net.Controllers.MobileApp;
using KgWebApi.Net.Data;
using KuGou.Net.Clients;
using KuGou.Net.ExternalPlaylists;
using KuGou.Net.Infrastructure;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Session;
using Microsoft.AspNetCore.Mvc;
using Pure.DI;
using Pure.DI.MS;
using static Pure.DI.Lifetime;

namespace KgWebApi.Net.Services;

public sealed partial class WebApiKuGouServiceProvider : ServiceProviderFactory<WebApiKuGouServiceProvider>
{
    [System.Diagnostics.Conditional("DI")]
    private static void Setup() => DI.Setup(nameof(WebApiKuGouServiceProvider))
        .Hint(Hint.OnCannotResolveContractTypeNameWildcard, "System.Net.Http.IHttpClientFactory")
        .Hint(Hint.OnCannotResolveContractTypeNameWildcard, "System.Net.Http.IHttpMessageHandlerFactory")

        .Roots<ControllerBase>()
        .Root<IKgWebSessionContext>()
        .Root<KgWebApiDbContext>()
        .Root<KgSessionManager>()
        .Root<RecommendClient>()
        .Root<RankClient>()
        .Root<SearchClient>()
        .Root<LoginClient>()
        .Root<PlaylistClient>()
        .Root<UserClient>()
        .Root<RegisterClient>()
        .Root<LyricClient>()
        .Root<AlbumClient>()
        .Root<SongClient>()
        .Root<ArtistClient>()
        .Root<CommentClient>()
        .Root<FmClient>()
        .Root<VideoClient>()
        .Root<LongAudioClient>()
        .Root<IpClient>()
        .Root<SceneClient>()
        .Root<ThemeClient>()
        .Root<ReportClient>()
        .Root<IExternalPlaylistParser>()

        .Bind<IKgWebSessionContext>().As(Scoped).To<KgWebSessionContext>()
        .Bind<ISessionPersistence>().As(Scoped).To<KgWebSessionPersistence>()
        .Bind<CookieContainer>().As(Scoped).To(_ => new CookieContainer())
        .Bind<KgSessionManager>().As(Scoped).To<KgSessionManager>()
        .Bind<KgSignatureHandler>().As(Scoped).To<KgSignatureHandler>()
        .Bind<IKgTransport>().As(Scoped).To<WebApiKgTransport>()

        .Bind<IExternalPlaylistParser>().As(Singleton).To<ExternalPlaylistParser>()
        .Bind<IExternalPlaylistParseStrategy>().As(Singleton).To<NeteasePlaylistParseStrategy>()
        .Bind<IExternalPlaylistParseStrategy>("QQ").As(Singleton).To<QqMusicPlaylistParseStrategy>()

        .Bind<ILogger<TT>>().To((ILoggerFactory loggerFactory) => loggerFactory.CreateLogger<TT>())

        .Bind<AlbumController>().To<AlbumController>()
        .Bind<ArtistController>().To<ArtistController>()
        .Bind<CaptchaController>().To<CaptchaController>()
        .Bind<CommentController>().To<CommentController>()
        .Bind<DiscoveryController>().To<DiscoveryController>()
        .Bind<ExternalPlaylistController>().To<ExternalPlaylistController>()
        .Bind<FmController>().To<FmController>()
        .Bind<LoginController>().To<LoginController>()
        .Bind<LyricController>().To<LyricController>()
        .Bind<MediaCatalogController>().To<MediaCatalogController>()
        .Bind<PlayListController>().To<PlayListController>()
        .Bind<RankController>().To<RankController>()
        .Bind<RegisterController>().To<RegisterController>()
        .Bind<ReportController>().To<ReportController>()
        .Bind<SearchController>().To<SearchController>()
        .Bind<SongController>().To<SongController>()
        .Bind<UserController>().To<UserController>()
        .Bind<YouthController>().To<YouthController>()
        .Bind<ApplicationInfo>().To<ApplicationInfo>();
}
