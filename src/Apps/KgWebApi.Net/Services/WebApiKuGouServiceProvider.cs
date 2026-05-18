using System.Net;
using Jab;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;

namespace KgWebApi.Net.Services;

[ServiceProvider(RootServices = new[]
{
    typeof(KgSessionManager),
    typeof(RecommendClient),
    typeof(RankClient),
    typeof(SearchClient),
    typeof(LoginClient),
    typeof(PlaylistClient),
    typeof(UserClient),
    typeof(RegisterClient),
    typeof(LyricClient),
    typeof(AlbumClient),
    typeof(SongClient),
    typeof(ArtistClient),
    typeof(CommentClient),
    typeof(FmClient),
    typeof(VideoClient),
    typeof(LongAudioClient),
    typeof(IpClient),
    typeof(SceneClient),
    typeof(ThemeClient),
    typeof(ReportClient)
})]
[Import<IKuGouServiceModule>]
[Singleton<ISessionPersistence>(Instance = nameof(SessionPersistence))]
[Singleton<CookieContainer>(Instance = nameof(CookieContainer))]
[Singleton<ILoggerFactory>(Instance = nameof(LoggerFactory))]
[Singleton<KgSessionManager>]
[Transient<KgSignatureHandler>]
[Singleton<IKgTransport>(Factory = nameof(CreateTransport))]
[Singleton<ILogger<RawLoginApi>>(Factory = nameof(CreateRawLoginApiLogger))]
[Singleton<ILogger<RawPlaylistApi>>(Factory = nameof(CreateRawPlaylistApiLogger))]
[Singleton<ILogger<RawDeviceApi>>(Factory = nameof(CreateRawDeviceApiLogger))]
[Singleton<ILogger<LoginClient>>(Factory = nameof(CreateLoginClientLogger))]
[Singleton<ILogger<RegisterClient>>(Factory = nameof(CreateRegisterClientLogger))]
public sealed partial class WebApiKuGouServiceProvider
{
    public required ISessionPersistence SessionPersistence { get; init; }

    public required CookieContainer CookieContainer { get; init; }

    public required ILoggerFactory LoggerFactory { get; init; }

    public required IHttpClientFactory HttpClientFactory { get; init; }

    public IKgTransport CreateTransport(CookieContainer cookieContainer, KgSignatureHandler signatureHandler)
    {
        return new WebApiKgTransport(HttpClientFactory, signatureHandler);
    }

    public ILogger<RawLoginApi> CreateRawLoginApiLogger() => LoggerFactory.CreateLogger<RawLoginApi>();

    public ILogger<RawPlaylistApi> CreateRawPlaylistApiLogger() => LoggerFactory.CreateLogger<RawPlaylistApi>();

    public ILogger<RawDeviceApi> CreateRawDeviceApiLogger() => LoggerFactory.CreateLogger<RawDeviceApi>();

    public ILogger<LoginClient> CreateLoginClientLogger() => LoggerFactory.CreateLogger<LoginClient>();

    public ILogger<RegisterClient> CreateRegisterClientLogger() => LoggerFactory.CreateLogger<RegisterClient>();
}
