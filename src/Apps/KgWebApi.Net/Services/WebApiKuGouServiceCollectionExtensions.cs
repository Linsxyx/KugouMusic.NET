using System.Net;
using KuGou.Net.Clients;
using KuGou.Net.ExternalPlaylists;
using KuGou.Net.Protocol.Session;

namespace KgWebApi.Net.Services;

public static class WebApiKuGouServiceCollectionExtensions
{
    public static IServiceCollection AddWebApiKuGouServices(this IServiceCollection services)
    {
        services.AddScoped<WebApiKuGouServiceProvider>(sp => new WebApiKuGouServiceProvider
        {
            SessionPersistence = sp.GetRequiredService<ISessionPersistence>(),
            CookieContainer = sp.GetRequiredService<CookieContainer>(),
            LoggerFactory = sp.GetRequiredService<ILoggerFactory>(),
            HttpClientFactory = sp.GetRequiredService<IHttpClientFactory>()
        });

        services.AddScopedFromKuGouProvider<KgSessionManager>();
        services.AddScopedFromKuGouProvider<RecommendClient>();
        services.AddScopedFromKuGouProvider<RankClient>();
        services.AddScopedFromKuGouProvider<SearchClient>();
        services.AddScopedFromKuGouProvider<LoginClient>();
        services.AddScopedFromKuGouProvider<PlaylistClient>();
        services.AddScopedFromKuGouProvider<UserClient>();
        services.AddScopedFromKuGouProvider<RegisterClient>();
        services.AddScopedFromKuGouProvider<LyricClient>();
        services.AddScopedFromKuGouProvider<AlbumClient>();
        services.AddScopedFromKuGouProvider<SongClient>();
        services.AddScopedFromKuGouProvider<ArtistClient>();
        services.AddScopedFromKuGouProvider<CommentClient>();
        services.AddScopedFromKuGouProvider<FmClient>();
        services.AddScopedFromKuGouProvider<VideoClient>();
        services.AddScopedFromKuGouProvider<LongAudioClient>();
        services.AddScopedFromKuGouProvider<IpClient>();
        services.AddScopedFromKuGouProvider<SceneClient>();
        services.AddScopedFromKuGouProvider<ThemeClient>();
        services.AddScopedFromKuGouProvider<ReportClient>();
        services.AddSingleton<IExternalPlaylistParser, ExternalPlaylistParser>();
        services.AddSingleton<IExternalPlaylistParseStrategy, NeteasePlaylistParseStrategy>();
        services.AddSingleton<IExternalPlaylistParseStrategy, QqMusicPlaylistParseStrategy>();

        return services;
    }

    private static IServiceCollection AddScopedFromKuGouProvider<TService>(this IServiceCollection services)
        where TService : class
    {
        services.AddScoped(sp => sp.GetRequiredService<WebApiKuGouServiceProvider>().GetService<TService>());
        return services;
    }
}
