using System.Net;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pure.DI;
using static Pure.DI.Lifetime;

namespace KuGou.Net.Infrastructure;

public sealed partial class KuGouServiceProvider
{
    public ISessionPersistence SessionPersistence { get; set; } = new InMemorySessionPersistence();

    public CookieContainer CookieContainer { get; set; } = new();

    public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

    private void Setup()
    {
        DI.Setup(nameof(KuGouServiceProvider))
            .Bind<ISessionPersistence>().As(Singleton).To(_ => SessionPersistence)
            .Bind<CookieContainer>().As(Singleton).To(_ => CookieContainer)
            .Bind<ILoggerFactory>().As(Singleton).To(_ => LoggerFactory)
            .Bind<KgSessionManager>().As(Singleton).To<KgSessionManager>()
            .Bind<KgSignatureHandler>().To<KgSignatureHandler>()
            .Bind<IKgTransport>().To((CookieContainer cookieContainer, KgSignatureHandler signatureHandler) =>
                CreateTransport(cookieContainer, signatureHandler))
            .Bind<ILogger<TT>>().As(Singleton).To((ILoggerFactory loggerFactory) => loggerFactory.CreateLogger<TT>())
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
            .Root<ReportClient>();
    }

    public TService GetService<TService>()
        where TService : class
    {
        return Resolve<TService>();
    }

    public static KgHttpTransport CreateTransport(CookieContainer cookieContainer, KgSignatureHandler signatureHandler)
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
