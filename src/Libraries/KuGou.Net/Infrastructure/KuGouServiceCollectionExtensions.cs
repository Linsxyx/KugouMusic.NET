using System.Net;
using KuGou.Net.Clients;
using KuGou.Net.Infrastructure.Http;
using KuGou.Net.Infrastructure.Http.Handlers;
using KuGou.Net.Protocol.Raw;
using KuGou.Net.Protocol.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KuGou.Net.Infrastructure;

public static class KuGouServiceCollectionExtensions
{
    public static IServiceCollection AddKuGouSdk(this IServiceCollection services)
    {
        services.TryAddSingleton<ISessionPersistence, InMemorySessionPersistence>();
        services.AddSingleton<CookieContainer>();
        services.AddSingleton<KgSessionManager>();


        services.AddTransient<KgSignatureHandler>();


        services.AddHttpClient<IKgTransport, KgHttpTransport>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var cookieContainer = sp.GetRequiredService<CookieContainer>();
                return new HttpClientHandler
                {
                    UseCookies = true,
                    CookieContainer = cookieContainer,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };
            })
            .AddHttpMessageHandler<KgSignatureHandler>();


        services.AddTransient<RawSearchApi>();
        services.AddTransient<RawLoginApi>();
        services.AddTransient<RawPlaylistApi>();
        services.AddTransient<RawUserApi>();
        services.AddTransient<RawDeviceApi>();
        services.AddTransient<RawLyricApi>();
        services.AddTransient<RawRankApi>();
        services.AddTransient<RawAlbumApi>();
        services.AddTransient<RawSongApi>();
        services.AddTransient<RawArtistApi>();
        services.AddTransient<RawCommentApi>();
        services.AddTransient<RawFmApi>();
        services.AddTransient<RawMediaCatalogApi>();
        services.AddTransient<RawReportApi>();

        services.AddTransient<RawDiscoveryApi>();


        services.AddTransient<RecommendClient>();


        services.AddTransient<RankClient>();
        services.AddTransient<SearchClient>();
        services.AddTransient<LoginClient>();
        services.AddTransient<PlaylistClient>();
        services.AddTransient<UserClient>();
        services.AddTransient<RegisterClient>();
        services.AddTransient<LyricClient>();
        services.AddTransient<AlbumClient>();
        services.AddTransient<SongClient>();
        services.AddTransient<ArtistClient>();
        services.AddTransient<CommentClient>();
        services.AddTransient<FmClient>();
        services.AddTransient<VideoClient>();
        services.AddTransient<LongAudioClient>();
        services.AddTransient<IpClient>();
        services.AddTransient<SceneClient>();
        services.AddTransient<ThemeClient>();
        services.AddTransient<ReportClient>();

        return services;
    }
}
