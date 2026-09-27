using System.Net;
using KuGou.Net.Clients;
using KuGou.Net.ExternalPlaylists;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;

namespace KgWebApi.Net.Services;

public static class WebApiKuGouServiceCollectionExtensions
{
    public static IServiceCollection AddWebApiKuGouServices(this IServiceCollection services)
    {
        services.AddScoped<IKgWebSessionContext, KgWebSessionContext>();
        services.AddScoped<ISessionPersistence, KgWebSessionPersistence>();
        services.AddScoped(_ => new CookieContainer());
        services.AddScoped<KgSessionManager>();
        services.AddScoped<KgSignatureHandler>();
        services.AddScoped<IKgTransport, WebApiKgTransport>();

        services.AddScoped<RawAlbumApi>();
        services.AddScoped<RawArtistApi>();
        services.AddScoped<RawCommentApi>();
        services.AddScoped<RawDeviceApi>();
        services.AddScoped<RawDiscoveryApi>();
        services.AddScoped<RawFmApi>();
        services.AddScoped<RawLoginApi>();
        services.AddScoped<RawVerificationApi>();
        services.AddScoped<RawLyricApi>();
        services.AddScoped<RawMediaCatalogApi>();
        services.AddScoped<RawPlaylistApi>();
        services.AddScoped<RawRankApi>();
        services.AddScoped<RawReportApi>();
        services.AddScoped<RawSearchApi>();
        services.AddScoped<RawSongApi>();
        services.AddScoped<RawUserApi>();

        services.AddScoped<AlbumClient>();
        services.AddScoped<ArtistClient>();
        services.AddScoped<CommentClient>();
        services.AddScoped<FmClient>();
        services.AddScoped<IpClient>();
        services.AddScoped<LongAudioClient>();
        services.AddScoped<LoginClient>();
        services.AddScoped<VerificationClient>();
        services.AddScoped<LyricClient>();
        services.AddScoped<PlaylistClient>();
        services.AddScoped<RankClient>();
        services.AddScoped<RecommendClient>();
        services.AddScoped<RegisterClient>();
        services.AddScoped<ReportClient>();
        services.AddScoped<SceneClient>();
        services.AddScoped<SearchClient>();
        services.AddScoped<SongClient>();
        services.AddScoped<ThemeClient>();
        services.AddScoped<UserClient>();
        services.AddScoped<VideoClient>();

        services.AddSingleton<IExternalPlaylistParser, ExternalPlaylistParser>();
        services.AddSingleton<IExternalPlaylistParseStrategy, NeteasePlaylistParseStrategy>();
        services.AddSingleton<IExternalPlaylistParseStrategy, QqMusicPlaylistParseStrategy>();

        return services;
    }
}
