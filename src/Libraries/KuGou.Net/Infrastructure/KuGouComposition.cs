using System.Net;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Session;
using Microsoft.Extensions.Logging;
using Pure.DI;
using static Pure.DI.Lifetime;

namespace KuGou.Net.Infrastructure;

public sealed partial class KuGouComposition
{
    [System.Diagnostics.Conditional("DI")]
    private static void Setup() => DI.Setup()
        .Hint(Hint.Resolve, "Off")
        .Root<KuGouApi>(nameof(Root))
        .Bind<KuGouApi>().As(Singleton).To<KuGouApi>()

        .Arg<ISessionPersistence>("sessionPersistence")
        .Arg<CookieContainer>("cookieContainer")
        .Arg<ILoggerFactory>("loggerFactory")
        .Bind<KgSessionManager>().As(Singleton).To<KgSessionManager>()
        .Bind<KgSignatureHandler>().To<KgSignatureHandler>()
        .Bind<HttpClient>().As(Singleton).To((CookieContainer cookieContainer, KgSignatureHandler signatureHandler) =>
            CreateHttpClient(cookieContainer, signatureHandler))
        .Bind<IKgTransport>().As(Singleton).To<KgHttpTransport>()
        .Bind<ILogger<TT>>().As(Singleton).To((ILoggerFactory loggerFactory) => loggerFactory.CreateLogger<TT>());

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

public readonly record struct KuGouApi(
    KgSessionManager SessionManager,
    RecommendClient Recommend,
    RankClient Rank,
    SearchClient Search,
    LoginClient Login,
    PlaylistClient Playlist,
    UserClient User,
    RegisterClient Register,
    LyricClient Lyric,
    AlbumClient Album,
    SongClient Song,
    ArtistClient Artist,
    CommentClient Comment,
    FmClient Fm,
    VideoClient Video,
    LongAudioClient LongAudio,
    IpClient Ip,
    SceneClient Scene,
    ThemeClient Theme,
    ReportClient Report);
